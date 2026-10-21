#!/usr/bin/env python3
"""Recover authoritative LJ source authors; dry-run unless --apply is supplied."""

import argparse
from collections import Counter
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sqlite3
from typing import NamedTuple
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET


HOST = "chgk-spb.livejournal.com"
RSS_URL = f"https://{HOST}/data/rss"
LJ_NS = "{http://www.livejournal.org/rss/lj/1.0/}"
MAX_BYTES = 5 * 1024 * 1024
MAX_ID = 2**63 - 1
AUTHOR_SCHEMA = """
CREATE TABLE IF NOT EXISTS lj_post_authors (
    normalized_link TEXT PRIMARY KEY,
    post_id INTEGER NOT NULL UNIQUE,
    lj_user_id INTEGER NOT NULL,
    lj_username TEXT NOT NULL
)
"""


class Author(NamedTuple):
    post_id: int
    user_id: int
    username: str


def positive_id(value):
    if isinstance(value, bool) or not re.fullmatch(r"[0-9]+", str(value)):
        raise ValueError("invalid_id")
    result = int(value)
    if not 0 < result <= MAX_ID:
        raise ValueError("invalid_id")
    return result


def login(value):
    if not isinstance(value, str):
        raise ValueError("invalid_login")
    value = value.strip().lower()
    if not re.fullmatch(r"[a-z0-9_-]{1,64}", value):
        raise ValueError("invalid_login")
    return value


def normalize_post_link(value):
    try:
        uri = urllib.parse.urlsplit(value)
        if (uri.scheme not in ("http", "https") or uri.hostname != HOST or
                uri.username is not None or uri.password is not None or
                uri.port not in (None, 443 if uri.scheme == "https" else 80)):
            return None
        match = re.fullmatch(r"/([0-9]+)\.html/?", uri.path)
        if not match:
            return None
        return f"https://{HOST}/{positive_id(match[1])}.html"
    except (TypeError, ValueError):
        return None


def validate_fetch_url(url):
    uri = urllib.parse.urlsplit(url)
    if (uri.scheme != "https" or uri.hostname != HOST or uri.username is not None or
            uri.password is not None or uri.port not in (None, 443)):
        raise ValueError("unsafe_destination")
    if uri.path != "/data/rss" and normalize_post_link(url) is None:
        raise ValueError("unsafe_destination")


class CommunityRedirectHandler(urllib.request.HTTPRedirectHandler):
    max_redirections = 3
    max_repeats = 2

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        validate_fetch_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def fetch(url, timeout=15):
    validate_fetch_url(url)
    request = urllib.request.Request(url, headers={"User-Agent": "WeekChgkSPB-author-backfill/1"})
    opener = urllib.request.build_opener(CommunityRedirectHandler())
    with opener.open(request, timeout=timeout) as response:
        validate_fetch_url(response.geturl())
        data = response.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise ValueError("response_too_large")
    return data


def _one_xml_value(item, tag):
    values = {element.text.strip() if element.text else "" for element in item.findall(tag)
              if not list(element)}
    if len(values) != 1 or any(list(element) for element in item.findall(tag)):
        raise ValueError("missing_or_conflicting_author")
    return next(iter(values))


def parse_rss(data):
    if b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValueError("unsafe_xml")
    root = ET.fromstring(data)
    authors, failures = {}, {}
    for item in root.findall("./channel/item"):
        link = normalize_post_link(item.findtext("link", ""))
        if link is None:
            continue
        try:
            username = login(_one_xml_value(item, LJ_NS + "poster"))
            user_id = positive_id(_one_xml_value(item, LJ_NS + "posterid"))
            rss_authors = item.findall("author")
            if any(list(author) or login(author.text or "") != username for author in rss_authors):
                raise ValueError("conflicting_author")
            author = Author(positive_id(urllib.parse.urlsplit(link).path[1:-5]), user_id, username)
            if link in failures or (link in authors and authors[link] != author):
                raise ValueError("conflicting_author")
            authors[link] = author
        except ValueError as error:
            authors.pop(link, None)
            failures[link] = str(error)
    return authors, failures


class EntryMetadataParser(HTMLParser):
    """Inspect document-level inline scripts, never article or comment contents."""

    VOID_TAGS = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link",
                 "meta", "param", "source", "track", "wbr"}

    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.stack = []
        self.in_script = False
        self.parts = []
        self.scripts = []

    def handle_starttag(self, tag, attrs):
        if tag == "script" and self.stack in (["html", "head"], ["html", "body"]) and not dict(attrs).get("src"):
            self.in_script = True
            self.parts = []
        if tag not in self.VOID_TAGS:
            self.stack.append(tag)

    def handle_endtag(self, tag):
        if tag == "script" and self.in_script:
            self.scripts.append("".join(self.parts))
            self.in_script = False
        if tag in self.stack:
            self.stack = self.stack[:len(self.stack) - 1 - self.stack[::-1].index(tag)]

    def handle_data(self, data):
        if self.in_script:
            self.parts.append(data)


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate_metadata_key")
        result[key] = value
    return result


def parse_page(data, link):
    parser = EntryMetadataParser()
    parser.feed(data.decode("utf-8"))
    decoder = json.JSONDecoder(object_pairs_hook=_unique_object)
    entries = []
    for script in parser.scripts:
        for match in re.finditer(r"^\s*Site\.entry\s*=\s*", script, re.MULTILINE):
            entry, end = decoder.raw_decode(script[match.end():])
            if not script[match.end() + end:].lstrip().startswith(";"):
                raise ValueError("invalid_entry_metadata")
            entries.append(entry)
    if len(entries) != 1 or not isinstance(entries[0], dict):
        raise ValueError("missing_or_conflicting_entry_metadata")
    entry = entries[0]
    post_id = positive_id(urllib.parse.urlsplit(link).path[1:-5])
    if entry.get("journal") != "chgk_spb" or positive_id(entry.get("ditemid")) != post_id:
        raise ValueError("wrong_source_post")
    return Author(post_id, positive_id(entry.get("posterid")), login(entry.get("poster")))


def existing_sources(connection):
    tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    links = set()
    for table in ("posts", "external_posts"):
        if table in tables:
            for (raw_link,) in connection.execute(f"SELECT link FROM {table}"):
                normalized = normalize_post_link(raw_link)
                if normalized:
                    links.add(normalized)
    authors = {}
    if "lj_post_authors" in tables:
        for link, post_id, user_id, username in connection.execute(
                "SELECT normalized_link,post_id,lj_user_id,lj_username FROM lj_post_authors"):
            authors[link] = Author(post_id, user_id, username)
    return links, authors


def collect(connection, fetcher=fetch):
    links, existing = existing_sources(connection)
    rss_error = None
    try:
        rss_authors, rss_failures = parse_rss(fetcher(RSS_URL))
    except (OSError, ValueError, ET.ParseError) as error:
        rss_authors, rss_failures = {}, {}
        rss_error = type(error).__name__
    links.update(rss_authors)
    links.update(rss_failures)
    candidates, report = {}, []
    for link in sorted(links):
        source = "rss" if link in rss_authors else "stored" if link in existing else "page"
        row = {"link": link, "source": source}
        try:
            if link in rss_failures:
                # Conflicting feed metadata must not fall back to a second identity source.
                raise ValueError(rss_failures[link])
            author = rss_authors.get(link) or existing.get(link) or parse_page(fetcher(link), link)
            old = existing.get(link)
            if old and old.user_id != author.user_id:
                row.update(status="conflict", reason="stored_author_differs")
            else:
                candidates[link] = author
                row.update(status="unchanged" if old == author else "would_save",
                           lj_user_id=author.user_id, lj_username=author.username)
        except (OSError, ValueError, ET.ParseError) as error:
            row.update(status="unresolved", reason=str(error) if isinstance(error, ValueError)
                       and not isinstance(error, json.JSONDecodeError) else type(error).__name__)
        report.append(row)
    return candidates, {"applied": False, "rss_error": rss_error, "results": report}


def apply(connection, candidates, report):
    with connection:
        connection.execute(AUTHOR_SCHEMA)
        for row in report["results"]:
            link = row["link"]
            if link not in candidates:
                continue
            author = candidates[link]
            try:
                cursor = connection.execute("""
                    INSERT INTO lj_post_authors(normalized_link,post_id,lj_user_id,lj_username)
                    VALUES(?,?,?,?)
                    ON CONFLICT(normalized_link) DO UPDATE SET lj_username=excluded.lj_username
                    WHERE lj_post_authors.lj_user_id=excluded.lj_user_id
                      AND lj_post_authors.post_id=excluded.post_id
                    """, (link, author.post_id, author.user_id, author.username))
                row["status"] = ("unchanged" if row["status"] == "unchanged" else "saved") if cursor.rowcount else "conflict"
            except sqlite3.IntegrityError:
                row.update(status="conflict", reason="stored_post_differs")
    report["applied"] = True


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, required=True)
    parser.add_argument("--apply", action="store_true", help="write confirmed source metadata; default is read-only")
    args = parser.parse_args(argv)
    try:
        database_uri = args.db.resolve().as_uri()
        with sqlite3.connect(database_uri + "?mode=ro", uri=True) as connection:
            candidates, report = collect(connection)
        if args.apply:
            with sqlite3.connect(database_uri + "?mode=rw", uri=True) as connection:
                apply(connection, candidates, report)
        report["counts"] = dict(Counter(row["status"] for row in report["results"]))
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    except (OSError, sqlite3.Error) as error:
        print(json.dumps({"error": type(error).__name__, "applied": False}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
