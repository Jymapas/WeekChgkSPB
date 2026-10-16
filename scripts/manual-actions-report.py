#!/usr/bin/env python3
"""Read-only report of manual bot activity and parser failure reasons."""
import argparse
import datetime as dt
import json
from pathlib import Path
import sqlite3
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--db', required=True, type=Path)
    parser.add_argument('--since', help='UTC date/time; defaults to the last 7 days')
    parser.add_argument('--post-id', type=int, help='RSS post or announcement ID')
    parser.add_argument('--limit', type=int, default=50, help='Maximum recent actions (1..1000)')
    parser.add_argument('--details', action='store_true', help='Include before/after value snapshots')
    args = parser.parse_args()
    if not 1 <= args.limit <= 1000:
        parser.error('--limit must be between 1 and 1000')
    try:
        since = dt.datetime.fromisoformat(args.since.replace('Z', '+00:00')) if args.since else dt.datetime.now(dt.timezone.utc) - dt.timedelta(days=7)
    except ValueError:
        parser.error('--since must be an ISO date/time')
    if since.tzinfo is None:
        since = since.replace(tzinfo=dt.timezone.utc)
    since_text = since.astimezone(dt.timezone.utc).isoformat().replace('+00:00', 'Z')
    with sqlite3.connect(args.db.resolve().as_uri() + '?mode=ro', uri=True) as connection:
        connection.row_factory = sqlite3.Row
        where = 'm.started_at_utc >= ?'
        params = [since_text]
        if args.post_id is not None:
            where += " AND ((m.entity_type IN ('announcement','review_draft') AND m.entity_id=?) OR m.source_link=?)"
            params.extend([args.post_id, f'https://chgk-spb.livejournal.com/{args.post_id}.html'])

        def query(sql, extra=()):
            return [dict(row) for row in connection.execute(sql, [*params, *extra])]

        report = {
            'since_utc': since_text,
            'post_id': args.post_id,
            'totals': query(f"""
                SELECT COUNT(*) AS actions, COUNT(DISTINCT user_id) AS users,
                    COALESCE(SUM(outcome='awaiting_input'), 0) AS repeated_steps,
                    COALESCE(SUM(outcome='error'), 0) AS errors,
                    COALESCE(SUM(completed_at_utc IS NULL), 0) AS unfinished
                FROM manual_actions m WHERE {where}
                """)[0],
            'frequent_actions': query(f"""
                SELECT action_kind, action_name, step_before, outcome, COUNT(*) AS count
                FROM manual_actions m WHERE {where}
                GROUP BY action_kind, action_name, step_before, outcome ORDER BY count DESC
                """),
            'parser_reasons': query(f"""
                SELECT a.failure_code, m.action_name, COUNT(*) AS actions,
                    COUNT(DISTINCT m.entity_id) AS posts
                FROM manual_actions m
                JOIN announcement_parse_attempts a ON a.post_id=m.entity_id
                    AND m.entity_type IN ('announcement','review_draft')
                WHERE {where}
                GROUP BY a.failure_code, m.action_name ORDER BY actions DESC
                """),
        }
        snapshots = ', before_json, after_json' if args.details else ''
        report['recent_actions'] = query(f"""
            SELECT m.id, m.update_id, m.user_id, m.chat_id, m.message_id, m.callback_query_id,
                m.started_at_utc, m.completed_at_utc, m.action_kind, m.action_name, m.handler,
                m.outcome, m.entity_type, m.entity_id, m.source_link,
                m.step_before, m.step_after, m.input_text, m.error_type {snapshots}
            FROM manual_actions m WHERE {where} ORDER BY m.id DESC LIMIT ?
            """, [args.limit])
        for row in report['recent_actions']:
            for column in ('before_json', 'after_json'):
                if row.get(column) is not None:
                    row[column] = json.loads(row[column])
        print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (sqlite3.Error, OSError) as error:
        print(f'Cannot read audit database: {error}', file=sys.stderr)
        sys.exit(1)
