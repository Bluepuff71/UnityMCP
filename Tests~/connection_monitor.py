import requests
import time
import sys

URL = "http://localhost:8080/"
PAYLOAD = {"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}}
DURATION_SECONDS = 30
POLL_INTERVAL_MS = 100

def main():
    errors = []
    iterations = (DURATION_SECONDS * 1000) // POLL_INTERVAL_MS

    for i in range(iterations):
        try:
            r = requests.post(URL, json=PAYLOAD, timeout=2)
            data = r.json()

            # Must be valid JSON-RPC (either result or error)
            if "result" not in data and "error" not in data:
                errors.append(f"Invalid response at {i}: {data}")

        except requests.exceptions.ConnectionError as e:
            errors.append(f"Connection error at iteration {i}: {e}")
        except Exception as e:
            errors.append(f"Unexpected error at {i}: {e}")

        time.sleep(POLL_INTERVAL_MS / 1000)

    if errors:
        print(f"FAILED: {len(errors)} errors")
        for e in errors[:10]:
            print(f"  - {e}")
        sys.exit(1)
    else:
        print(f"PASSED: {iterations} requests, zero connection errors")
        sys.exit(0)

if __name__ == "__main__":
    main()
