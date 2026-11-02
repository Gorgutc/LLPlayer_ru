"""Deterministic lifecycle tests. No model imports, child processes, networking or OS signals."""
import signal
import unittest
from unittest.mock import patch

import server


class LifecycleTests(unittest.TestCase):
    def tearDown(self):
        server._OWN_POSIX_SESSION = None

    def test_proc_start_time_handles_parenthesized_command_and_reused_pid(self):
        stat = "123 (player ) with spaces) " + " ".join(["S"] + ["0"] * 18 + ["887766"])
        self.assertEqual("887766", server._proc_start_time(stat))
        self.assertIsNone(server._proc_start_time("malformed"))
        with patch.object(server, "_read_parent_start_time", return_value="887767"):
            self.assertFalse(server._parent_identity_matches(123, "887766"))

    def test_managed_session_requires_same_original_parent_and_owns_only_its_group(self):
        with patch.object(server.sys, "platform", "linux"), \
                patch.object(signal, "SIGKILL", 9, create=True), \
                patch.object(server.os, "getppid", return_value=123), \
                patch.object(server, "_read_parent_start_time", return_value="887766"), \
                patch.object(server.os, "setsid", create=True) as setsid, \
                patch.object(server.os, "getpid", return_value=456), \
                patch.object(server.os, "getsid", return_value=456, create=True), \
                patch.object(server.os, "getpgrp", return_value=456, create=True), \
                patch.object(server.os, "killpg", create=True) as killpg, \
                patch.object(server.os, "_exit") as exit_process:
            server._establish_managed_session(123, "887766")
            setsid.assert_called_once_with()
            server._exit_owned_run()
            killpg.assert_called_once_with(456, 9)
            exit_process.assert_called_once_with(0)

    def test_reused_parent_or_foreign_group_never_receives_a_group_signal(self):
        with patch.object(server.sys, "platform", "linux"), \
                patch.object(server.os, "getppid", return_value=123), \
                patch.object(server, "_read_parent_start_time", return_value="new-identity"), \
                patch.object(server.os, "setsid", create=True) as setsid:
            with self.assertRaises(RuntimeError):
                server._establish_managed_session(123, "old-identity")
            setsid.assert_not_called()
        server._OWN_POSIX_SESSION = 456
        with patch.object(server.os, "getpid", return_value=456), \
                patch.object(server.os, "getsid", return_value=999, create=True), \
                patch.object(server.os, "getpgrp", return_value=999, create=True), \
                patch.object(server.os, "killpg", create=True) as killpg, \
                patch.object(server.os, "_exit") as exit_process:
            server._exit_owned_run()
            killpg.assert_not_called()
            exit_process.assert_called_once_with(0)

    def test_manual_run_only_exits_self(self):
        with patch.object(server.os, "killpg", create=True) as killpg, \
                patch.object(server.os, "_exit") as exit_process:
            server._exit_owned_run()
            killpg.assert_not_called()
            exit_process.assert_called_once_with(0)


if __name__ == "__main__":
    unittest.main()
