import threading
import unittest
from unittest import mock

from tests.helpers import wait_until
from tests.test_flow import RelayCase
from tools import pult_cli
from tools.pult_testclient import ClientError, Phone


class ListenCase(RelayCase):
    clock_fake = False

    def start(self, peer, auto=False, seconds=6):
        lines = []
        patcher = mock.patch.object(pult_cli, "say", lambda *parts: lines.append(" ".join(str(p) for p in parts)))
        patcher.start()
        self.addCleanup(patcher.stop)
        thread = threading.Thread(target=pult_cli.listen, args=(peer, auto, seconds, 0.05), daemon=True)
        thread.start()
        self.addCleanup(thread.join, 10)
        return lines

    def seen(self, lines, text):
        return any(text in line for line in lines)


class ListenTest(ListenCase):
    def test_listen_keeps_reading_after_a_reconnect(self):
        pc, phone = self.relay.paired()
        first = phone.stream_id
        lines = self.start(phone)
        rival = Phone.restore(phone.state_dump())
        self.relay.peers.append(rival)
        rival.login()
        wait_until(lambda: phone.stream_id not in (None, first), 5)
        pc.send({"type": "text_request", "request_id": "after-reconnect"})
        wait_until(lambda: self.seen(lines, "after-reconnect"), 5)

    def test_failed_receive_reconnects_instead_of_stopping(self):
        pc, phone = self.relay.paired()
        real = phone.ack
        calls = []

        def flaky(ids):
            calls.append(1)
            if len(calls) == 1:
                raise ClientError("502")
            return real(ids)

        phone.ack = flaky
        lines = self.start(phone)
        pc.send({"type": "text_request", "request_id": "first"})
        wait_until(lambda: self.seen(lines, "receive failed"), 5)
        wait_until(lambda: self.seen(lines, "reconnected"), 5)
        wait_until(lambda: self.seen(lines, '"first"'), 5)

    def test_failed_auto_reply_does_not_stop_listening(self):
        pc, phone = self.relay.paired()
        real = phone.send
        calls = []

        def flaky(body, *args, **kwargs):
            calls.append(1)
            if len(calls) == 1:
                raise ClientError("429")
            return real(body, *args, **kwargs)

        phone.send = flaky
        lines = self.start(phone, auto=True)
        pc.send({"type": "text_request", "request_id": "one"})
        wait_until(lambda: self.seen(lines, "auto-reply failed"), 5)
        pc.send({"type": "text_request", "request_id": "two"})
        wait_until(lambda: len(calls) >= 2, 5)
        wait_until(lambda: self.seen(lines, "auto-reply phone-"), 5)


if __name__ == "__main__":
    unittest.main()
