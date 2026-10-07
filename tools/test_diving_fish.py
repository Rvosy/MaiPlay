import unittest
from unittest.mock import patch

import diving_fish as api


class UploadTests(unittest.TestCase):
    def setUp(self):
        self.music = [{"id": "383", "title": "Link(CoF)", "type": "SD",
                       "charts": [{"notes": [2, 1, 1, 1]}] * 5}]
        self.chart = {"path": "01_version/000383_Link/maidata.txt", "notes": 5,
                      "status": "partial", "achievement": 98.1234, "dxScore": 12}
        self.report = {"difficulty": 5, "charts": [self.chart]}

    def test_uses_catalog_title_and_maps_master(self):
        records, excluded = api.prepare(self.report, self.music)
        self.assertEqual([], excluded)
        self.assertEqual(("Link(CoF)", 3, "", ""),
                         tuple(records[0][k] for k in ("title", "level_index", "fc", "fs")))

    def test_partial_included_but_errors_and_version_mismatch_excluded(self):
        self.chart["status"] = "error"
        self.assertEqual(0, len(api.prepare(self.report, self.music)[0]))
        self.chart["status"] = "partial"
        self.chart["notes"] = 6
        self.assertEqual(0, len(api.prepare(self.report, self.music)[0]))

    def test_comparison_side_is_explicit(self):
        metrics = dict(self.chart, status="all-cp", achievement=101, dxScore=15)
        self.chart["candidate"] = metrics
        records, _ = api.prepare(self.report, self.music, "candidate")
        self.assertEqual((101, "app"), (records[0]["achievements"], records[0]["fc"]))

    def test_readback_detects_skips_and_lower_score(self):
        records, _ = api.prepare(self.report, self.music)
        self.assertEqual([], api.verify(records, {"records": records}))
        self.assertTrue(api.verify(records, {"records": []}))
        self.assertTrue(api.verify(records, {"records": [dict(records[0], achievements=97)]}))

    def test_wrong_account_never_posts(self):
        records, _ = api.prepare(self.report, self.music)
        with patch("sys.argv", ["diving_fish", "upload", "bundle.json", "receipt", "--account", "evaluation"]), \
             patch.object(api, "read", return_value={"records": records}), \
             patch.dict(api.os.environ, {"MAIPLAY_DIVING_FISH_IMPORT_TOKEN": "test-only"}), \
             patch.object(api, "request", return_value={"username": "other"}) as request:
            with self.assertRaisesRegex(ValueError, "account"):
                api.main()
            request.assert_called_once_with("/player/records", "test-only")

    def test_only_master_and_remaster_are_prepared(self):
        self.report["charts"] = [dict(self.chart, difficulty=d) for d in range(1, 8)]
        records, excluded = api.prepare(self.report, self.music)
        self.assertEqual([3, 4], [r["level_index"] for r in records])
        self.assertEqual([1, 2, 3, 4, 7], [r["difficulty"] for r in excluded])

    def test_old_expert_bundle_is_rejected_before_network_access(self):
        with patch("sys.argv", ["diving_fish", "upload", "bundle.json", "receipt", "--account", "evaluation"]), \
             patch.object(api, "read", return_value={"records": [{"level_index": 2}]}), \
             patch.object(api, "request") as request:
            with self.assertRaisesRegex(ValueError, "Only Master and Re:Master"):
                api.main()
            request.assert_not_called()


if __name__ == "__main__":
    unittest.main()
