import json
import unittest
from pathlib import Path


FIXTURE_DIR = Path(__file__).parent / "fixtures" / "fw_4_1_13"


class ProtocolState:
    def __init__(self):
        self.LastScene = "Unknown"
        self.LastBrightness = 0
        self.LastSpeed = 0
        self.LastStatus = "Disconnected"
        self.LastPowerStatus = ""
        self.LastAckStatus = ""

    def update_last_status(self):
        if self.LastPowerStatus:
            self.LastStatus = self.LastPowerStatus
            if self.LastAckStatus:
                self.LastStatus += f" | {self.LastAckStatus}"
            return

        if self.LastAckStatus:
            self.LastStatus = self.LastAckStatus

    def handle_inbound_websocket_json(self, frame: str):
        if not frame or '"cmd":"fromCtlr"' not in frame:
            return

        payload = json.loads(frame)

        run_pattern = payload.get("runPattern")
        if isinstance(run_pattern, dict):
            run_pattern_file = run_pattern.get("file")
            if run_pattern_file:
                self.LastScene = run_pattern_file

            run_pattern_data = run_pattern.get("data")
            if run_pattern_data:
                parsed_data = json.loads(run_pattern_data)
                if "brightness" in parsed_data:
                    self.LastBrightness = int(parsed_data["brightness"])
                if "speed" in parsed_data:
                    self.LastSpeed = int(parsed_data["speed"])

            zone_id = run_pattern.get("id")
            zone_name_list = run_pattern.get("zoneName")
            first_zone = zone_name_list[0] if isinstance(zone_name_list, list) and zone_name_list else ""
            zone = zone_id or first_zone
            self.LastAckStatus = f"RunPattern ack: {zone}" if zone else "RunPattern update received"

        if "brightness" in payload:
            self.LastBrightness = int(payload["brightness"])

        if "ledPower" in payload:
            self.LastPowerStatus = "LED power is ON" if bool(payload["ledPower"]) else "LED power is OFF"

        self.update_last_status()
        if not self.LastStatus or self.LastStatus == "Disconnected":
            self.LastStatus = "fromCtlr update received"


def load_fixture(name: str) -> str:
    return (FIXTURE_DIR / name).read_text().strip()


class HandleInboundWebSocketJsonFixtureTests(unittest.TestCase):
    def test_runpattern_ack_per_zone_updates_scene_and_status(self):
        state = ProtocolState()

        state.handle_inbound_websocket_json(load_fixture("fromCtlr_runPattern_ack_per_zone.json"))

        self.assertEqual("Holiday/Classic.json", state.LastScene)
        self.assertEqual("RunPattern ack: Garage Door", state.LastStatus)

    def test_advanced_runpattern_data_updates_brightness_and_speed(self):
        state = ProtocolState()

        state.handle_inbound_websocket_json(load_fixture("fromCtlr_runPattern_advanced_escaped_data.json"))

        self.assertEqual("Holiday/Candy Cane.json", state.LastScene)
        self.assertEqual(63, state.LastBrightness)
        self.assertEqual(7, state.LastSpeed)
        self.assertEqual("RunPattern ack: Garage Door", state.LastStatus)

    def test_ledpower_true_and_false_updates_status(self):
        state = ProtocolState()

        state.handle_inbound_websocket_json(load_fixture("fromCtlr_ledPower_true.json"))
        self.assertEqual("LED power is ON", state.LastStatus)

        state.handle_inbound_websocket_json(load_fixture("fromCtlr_ledPower_false.json"))
        self.assertEqual("LED power is OFF", state.LastStatus)

    def test_mixed_event_sequence_interleaving_preserves_status_aggregation(self):
        state = ProtocolState()

        sequence = [
            "fromCtlr_runPattern_ack_per_zone.json",
            "fromCtlr_ledPower_true.json",
            "fromCtlr_runPattern_advanced_escaped_data.json",
            "fromCtlr_ledPower_false.json",
            "fromCtlr_runPattern_ack_per_zone.json",
        ]

        for fixture in sequence:
            state.handle_inbound_websocket_json(load_fixture(fixture))

        self.assertEqual("Holiday/Classic.json", state.LastScene)
        self.assertEqual(63, state.LastBrightness)
        self.assertEqual(7, state.LastSpeed)
        self.assertEqual("LED power is OFF | RunPattern ack: Garage Door", state.LastStatus)


if __name__ == "__main__":
    unittest.main()
