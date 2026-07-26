import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "scripts"))

from issue_queue import collect_linked_issue_numbers, is_issue_available


class IssueQueueTests(unittest.TestCase):
    def test_collect_linked_issue_numbers_uses_closing_issue_references(self) -> None:
        pull_requests = [
            {"closingIssuesReferences": [{"number": 12}, {"number": 25}]},
            {"closingIssuesReferences": [{"number": 12}]},
            {"closingIssuesReferences": []},
        ]

        self.assertEqual({12, 25}, collect_linked_issue_numbers(pull_requests))

    def test_is_issue_available_excludes_issues_with_linked_pull_requests(self) -> None:
        summary = {"number": 12, "state": "OPEN", "is_blocked": False}

        self.assertTrue(is_issue_available(summary, set()))
        self.assertFalse(is_issue_available(summary, {12}))

        blocked_summary = {"number": 13, "state": "OPEN", "is_blocked": True}
        self.assertFalse(is_issue_available(blocked_summary, set()))


if __name__ == "__main__":
    unittest.main()
