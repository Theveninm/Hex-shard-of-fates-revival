#!/usr/bin/env python3
"""End-to-end check of a running master server: python3 smoke_test.py [host] [port]

Registers two throwaway accounts, logs in again with the password, chats, challenges, relays game data both ways
and checks the protections (wrong password, unknown message, relay outside a match).
The accounts stay in the server's data: run it against a local or test server, not the public one.
"""
import base64, hashlib, hmac, json, os, socket, struct, sys, uuid

HOST = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 7777
JSON, RELAY = 1, 2


class Client:
    def __init__(self):
        self.sock = socket.create_connection((HOST, PORT), timeout=5)
        self.hello = self.expect("hello")

    def send(self, kind, payload):
        self.sock.sendall(struct.pack(">iB", len(payload), kind) + payload)

    def msg(self, **m):
        self.send(JSON, json.dumps(m).encode())

    def read(self):
        head = self._exactly(5)
        if head is None:
            return None
        length, kind = struct.unpack(">iB", head)
        payload = self._exactly(length)
        return (kind, json.loads(payload) if kind == JSON else payload)

    def _exactly(self, n):
        data = b""
        while len(data) < n:
            chunk = self.sock.recv(n - len(data))
            if not chunk:
                return None
            data += chunk
        return data

    def expect(self, t):
        """Next JSON message of type t (other lobby traffic in between is skipped)."""
        while True:
            frame = self.read()
            assert frame is not None, f"closed while waiting for {t}"
            kind, m = frame
            if kind == JSON and m["t"] == t:
                return m
            if kind == JSON and m["t"] == "error" and t != "error":
                raise AssertionError(f"error while waiting for {t}: {m}")

    def key(self, player_id, password):
        # Same derivation as the game client: the password never leaves the PC.
        return hashlib.pbkdf2_hmac("sha1", password.encode(), player_id.encode(), self.hello["iterations"], 32)

    def register(self, player_id, name, password):
        self.msg(t="register", v=1, id=player_id, name=name, key=base64.b64encode(self.key(player_id, password)).decode())

    def login(self, player_id, password, name=None):
        proof = hmac.new(self.key(player_id, password), base64.b64decode(self.hello["nonce"]), hashlib.sha256).digest()
        self.msg(t="login", v=1, id=player_id, name=name, proof=base64.b64encode(proof).decode())


def account():
    pid = uuid.uuid4().hex
    return pid, "T" + pid[:8], base64.b64encode(os.urandom(12)).decode()


a_id, a_name, a_pw = account()
b_id, b_name, b_pw = account()

a = Client(); a.register(a_id, a_name, a_pw); a.expect("welcome")
b = Client(); b.register(b_id, b_name, b_pw); b.expect("welcome")
assert a.expect("joined")["player"]["name"] == b_name
print("register: ok")

# Second connection of the same account is refused; a wrong password too.
c = Client(); c.login(a_id, a_pw); assert c.expect("error")["code"] == "already_online"
c = Client(); c.login(b_id, "wrong"); assert c.expect("error")["code"] == "bad_password"
print("login refusals: ok")

# Log in again with the password (new nonce each connection).
b.sock.close(); assert a.expect("left")["id"] == b_id
b = Client(); b.login(b_id, b_pw); w = b.expect("welcome")
assert any(p["id"] == a_id for p in w["players"])
print("login: ok")

b.msg(t="chat", text="hello")
assert a.expect("chat")["text"] == "hello"
b.msg(t="bogus"); assert b.expect("error")["code"] == "bad_request"
print("chat: ok")

# Relay before a match is ignored, then challenge -> accept -> match.
a.send(RELAY, b"ignored")
a.msg(t="challenge", to=b_id, format="immortal")
a.expect("challenge_sent")
assert b.expect("challenged")["from"] == a_id
b.msg(t="accept", **{"from": a_id})
ma, mb = a.expect("match"), b.expect("match")
assert (ma["seat"], mb["seat"]) == (0, 1) and ma["opponent"]["id"] == b_id
print("challenge: ok")

big = os.urandom(200_000)
a.send(RELAY, big)
kind, data = b.read()
while kind != RELAY:
    kind, data = b.read()
assert data == big, "relay a->b"
b.send(RELAY, b"transaction")
kind, data = a.read()
while kind != RELAY:
    kind, data = a.read()
assert data == b"transaction", "relay b->a"
print("relay: ok")

b.msg(t="leave")
assert a.expect("match_end")["reason"] == "left"
print("leave: ok")

# Open game: b creates, a sees it and joins, b hosts.
b.msg(t="create", format="immortal")
g = a.expect("game_added")["game"]
assert g["host"] == b_id
a.msg(t="join", game=g["id"])
ma, mb = a.expect("match"), b.expect("match")
assert (mb["seat"], ma["seat"]) == (0, 1)
a.msg(t="leave")
b.expect("match_end")
print("open game: ok")
# Names: taken by another account (case, spaces, '_' and '-' ignored) or reserved by the server.
c_id, _, c_pw = account()
c = Client(); c.register(c_id, a_name.upper().replace("T", "T_", 1), c_pw); assert c.expect("error")["code"] == "name_taken"
c = Client(); c.register(c_id, "Ad-Min", c_pw); assert c.expect("error")["code"] == "name_taken"
c = Client(); c.register(c_id, "C" + c_id[:8], c_pw); c.expect("welcome")
c.sock.close()
c = Client(); c.login(c_id, c_pw, name=b_name.lower())
assert c.expect("notice")["code"] == "name_taken" and c.expect("welcome")["name"] == "C" + c_id[:8]
print("names: ok")
print("ALL OK")
