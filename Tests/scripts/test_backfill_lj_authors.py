import importlib.util
import io
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch
import urllib.request


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "backfill-lj-authors.py"
SPEC = importlib.util.spec_from_file_location("backfill_lj_authors", SCRIPT)
backfill = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(backfill)
LINK = "https://chgk-spb.livejournal.com/123.html"


def feed(metadata='<author>author</author><lj:poster>author</lj:poster><lj:posterid>7</lj:posterid>'):
    return (f'<rss xmlns:lj="http://www.livejournal.org/rss/lj/1.0/"><channel>'
            f'<item><link>{LINK}</link>{metadata}</item></channel></rss>').encode()


def page(entry=None, before="", after=""):
    entry = entry or {"journal": "chgk_spb", "ditemid": 123, "posterid": 7, "poster": "author"}
    return (f'<html><head>{before}<script>\nSite.entry = {json.dumps(entry)};\n</script></head>'
            f'<body>{after}</body></html>').encode()


class AuthorBackfillTests(unittest.TestCase):
    def setUp(self):
        self.connection = sqlite3.connect(":memory:")
        self.addCleanup(self.connection.close)
        self.connection.execute("CREATE TABLE posts(id INTEGER PRIMARY KEY,link TEXT)")
        self.connection.execute("CREATE TABLE external_posts(announcementId INTEGER PRIMARY KEY,link TEXT)")

    def test_normalizes_only_community_post_urls(self):
        self.assertEqual(LINK, backfill.normalize_post_link("http://CHGK-SPB.livejournal.com/123.html?utm_source=test#comment"))
        for value in ("https://example.org/123.html", "https://chgk-spb.livejournal.com.evil.test/123.html",
                      "https://user@chgk-spb.livejournal.com/123.html", "https://chgk-spb.livejournal.com:8080/123.html",
                      "file:///123.html", "https://chgk-spb.livejournal.com/0.html", "https://chgk-spb.livejournal.com/999999999999999999999.html"):
            with self.subTest(value=value):
                self.assertIsNone(backfill.normalize_post_link(value))

    def test_rss_reads_author_and_rejects_conflicts(self):
        authors, failures = backfill.parse_rss(feed())
        self.assertEqual(backfill.Author(123, 7, "author"), authors[LINK])
        self.assertFalse(failures)
        for metadata in ("<lj:poster>author</lj:poster>",
                         "<lj:poster>author</lj:poster><lj:posterid>0</lj:posterid>",
                         "<lj:poster>author</lj:poster><lj:posterid>7</lj:posterid><lj:posterid>8</lj:posterid>",
                         "<lj:poster>author</lj:poster><lj:posterid>7</lj:posterid><author>someone_else</author>"):
            with self.subTest(metadata=metadata):
                authors, failures = backfill.parse_rss(feed(metadata))
                self.assertNotIn(LINK, authors)
                self.assertIn(LINK, failures)

    def test_rss_duplicate_items_with_different_authors_rejected(self):
        xml = feed().replace(b"</channel>", b'<item><link>' + LINK.encode() +
                           b'</link><lj:poster>other</lj:poster><lj:posterid>8</lj:posterid></item></channel>')
        authors, failures = backfill.parse_rss(xml)
        self.assertFalse(authors)
        self.assertIn(LINK, failures)

    def test_page_uses_entry_metadata_and_ignores_body_and_comments(self):
        fake = '<script>\nSite.entry = {"posterid":999,"poster":"attacker"};\n</script>'
        author = backfill.parse_page(page(after="<article>" + fake + "</article><!-- " + fake + " -->"), LINK)
        self.assertEqual(backfill.Author(123, 7, "author"), author)
        with self.assertRaises(ValueError):
            backfill.parse_page(f"<html><head></head><body><article>{fake}</article></body></html>".encode(), LINK)

    def test_page_accepts_document_metadata_in_body_footer(self):
        body_metadata = page().decode().split("<head>")[1].split("</head>")[0]
        data = f"<html><head></head><body><article>Source post</article>{body_metadata}</body></html>".encode()
        self.assertEqual(backfill.Author(123, 7, "author"), backfill.parse_page(data, LINK))

    def test_page_checks_identity_of_exact_source_and_duplicate_metadata(self):
        for entry in ({"journal": "someone", "ditemid": 123, "posterid": 7, "poster": "author"},
                      {"journal": "chgk_spb", "ditemid": 456, "posterid": 7, "poster": "author"},
                      {"journal": "chgk_spb", "ditemid": 123, "posterid": -1, "poster": "author"}):
            with self.subTest(entry=entry), self.assertRaises(ValueError):
                backfill.parse_page(page(entry), LINK)
        fake = '<script>\nSite.entry = {"posterid":999,"poster":"attacker"};\n</script>'
        with self.assertRaises(ValueError):
            backfill.parse_page(page(before=fake), LINK)

    def test_redirect_handler_rejects_cross_host_and_https_downgrade(self):
        handler = backfill.CommunityRedirectHandler()
        request = urllib.request.Request(LINK)
        for destination in ("https://evil.test/123.html", "http://chgk-spb.livejournal.com/123.html",
                            "https://chgk-spb.livejournal.com/profile/"):
            with self.subTest(destination=destination), self.assertRaises(ValueError):
                handler.redirect_request(request, None, 302, "Found", {}, destination)

    def test_dry_run_reads_feed_then_legacy_external_source_without_writing(self):
        legacy = "https://chgk-spb.livejournal.com/456.html"
        self.connection.execute("INSERT INTO external_posts VALUES(999,?)", (legacy,))
        calls = []

        def fetcher(url):
            calls.append(url)
            return feed() if url == backfill.RSS_URL else page({
                "journal": "chgk_spb", "ditemid": 456, "posterid": 8, "poster": "legacy"})

        candidates, report = backfill.collect(self.connection, fetcher)
        self.assertEqual([backfill.RSS_URL, legacy], calls)
        self.assertEqual(2, len(candidates))
        self.assertFalse(report["applied"])
        self.assertFalse(self.connection.execute("SELECT name FROM sqlite_master WHERE name='lj_post_authors'").fetchall())

    def test_apply_creates_schema_and_is_idempotent_without_granting_user_rights(self):
        candidates, report = backfill.collect(self.connection, lambda url: feed())
        backfill.apply(self.connection, candidates, report)
        self.assertEqual((LINK, 123, 7, "author"), self.connection.execute("SELECT * FROM lj_post_authors").fetchone())
        candidates, report = backfill.collect(self.connection, lambda url: feed())
        backfill.apply(self.connection, candidates, report)
        self.assertEqual(1, self.connection.execute("SELECT count(*) FROM lj_post_authors").fetchone()[0])
        self.assertEqual("unchanged", report["results"][0]["status"])
        self.assertFalse(self.connection.execute("SELECT name FROM sqlite_master WHERE name='allowed_users'").fetchall())

    def test_conflicting_stored_author_and_concurrent_write_are_not_overwritten(self):
        self.connection.execute(backfill.AUTHOR_SCHEMA)
        self.connection.execute("INSERT INTO lj_post_authors VALUES(?,123,8,'original')", (LINK,))
        candidates, report = backfill.collect(self.connection, lambda url: feed())
        self.assertFalse(candidates)
        self.assertEqual("conflict", report["results"][0]["status"])
        # The same check must hold when a competing writer appears after the dry-run.
        report = {"applied": False, "results": [{"link": LINK, "status": "would_save"}]}
        backfill.apply(self.connection, {LINK: backfill.Author(123, 7, "author")}, report)
        self.assertEqual("conflict", report["results"][0]["status"])
        self.assertEqual(8, self.connection.execute("SELECT lj_user_id FROM lj_post_authors").fetchone()[0])

    def test_network_failure_leaves_unknown_author_unresolved(self):
        self.connection.execute("INSERT INTO posts VALUES(123,?)", (LINK,))

        def unavailable(url):
            raise OSError("network data must not appear in report")

        candidates, report = backfill.collect(self.connection, unavailable)
        self.assertFalse(candidates)
        self.assertEqual("OSError", report["rss_error"])
        self.assertEqual("OSError", report["results"][0]["reason"])

    def test_cli_dry_run_leaves_database_bytes_unchanged_and_does_not_create_missing_db(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "posts.db"
            with sqlite3.connect(path) as connection:
                connection.execute("CREATE TABLE posts(id INTEGER PRIMARY KEY,link TEXT)")
            before = path.read_bytes()
            with patch.object(backfill, "collect", return_value=({}, {"results": [], "applied": False})), patch("sys.stdout", new=io.StringIO()):
                self.assertEqual(0, backfill.main(["--db", str(path)]))
            self.assertEqual(before, path.read_bytes())
            missing = Path(temporary) / "missing.db"
            with patch("sys.stdout", new=io.StringIO()):
                self.assertEqual(1, backfill.main(["--db", str(missing), "--apply"]))
            self.assertFalse(missing.exists())


if __name__ == "__main__":
    unittest.main()
