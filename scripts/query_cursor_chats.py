"""Query Cursor's state.vscdb for chat/conversation history."""
import sqlite3
import json
import os
import sys

DB_PATH = r"C:\Users\danjo\AppData\Roaming\Cursor\User\globalStorage\state.vscdb"

def extract_text_from_richtext(rt):
    """Extract plain text from Lexical richText JSON."""
    if not rt:
        return ""
    try:
        data = json.loads(rt) if isinstance(rt, str) else rt
        texts = []
        def walk(node):
            if isinstance(node, dict):
                if node.get("type") == "text" and "text" in node:
                    texts.append(node["text"])
                for k, v in node.items():
                    if k == "children":
                        for c in v:
                            walk(c)
                    else:
                        walk(v)
            elif isinstance(node, list):
                for x in node:
                    walk(x)
        walk(data)
        return "\n".join(texts) if texts else ""
    except Exception:
        return str(rt)[:500]

def main():
    if not os.path.exists(DB_PATH):
        print(f"Database not found: {DB_PATH}")
        return

    try:
        conn = sqlite3.connect(DB_PATH, timeout=5)
    except sqlite3.OperationalError as e:
        if "locked" in str(e).lower() or "busy" in str(e).lower():
            print("ERROR: Database is locked. Please CLOSE CURSOR completely, then run this script again.")
        else:
            print(f"Could not open database: {e}")
        return

    cur = conn.cursor()

    # List tables
    cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = [r[0] for r in cur.fetchall()]
    print("Tables:", tables)
    print()

    for tbl in tables:
        try:
            cur.execute(f"PRAGMA table_info({tbl})")
            cols = cur.fetchall()
            col_names = [c[1] for c in cols]
            print(f"--- Table: {tbl} (columns: {col_names}) ---")

            # Get row count
            cur.execute(f"SELECT COUNT(*) FROM {tbl}")
            count = cur.fetchone()[0]
            print(f"Row count: {count}")

            # Find key column (usually 'key' in ItemTable)
            key_col = "key" if "key" in col_names else col_names[0]
            value_col = "value" if "value" in col_names else (col_names[1] if len(col_names) > 1 else None)

            # Get sample keys - look for chat-related
            cur.execute(f"SELECT {key_col} FROM {tbl} LIMIT 200")
            keys = [r[0] for r in cur.fetchall()]

            chat_keys = [k for k in keys if k and any(x in str(k).lower() for x in ["chat", "composer", "conversation", "aichat", "bubble"])]
            print(f"Chat-related keys (sample): {chat_keys[:20]}")
            print()

            # Search for ashlands/reborn in keys
            cur.execute(f"SELECT {key_col} FROM {tbl} WHERE LOWER({key_col}) LIKE '%ashlands%' OR LOWER({key_col}) LIKE '%reborn%' OR LOWER({key_col}) LIKE '%valheim%'")
            ashlands_keys = cur.fetchall()
            if ashlands_keys:
                print(f"Keys containing ashlands/reborn/valheim: {ashlands_keys}")

            # If we have key and value, search value content
            if value_col:
                cur.execute(f"SELECT {key_col}, {value_col} FROM {tbl}")
                for row in cur.fetchall():
                    key, val = row[0], row[1]
                    if val and isinstance(val, str) and ("ashlands" in val.lower() or "reborn" in val.lower()):
                        # Check if it looks like a user message (initial prompt)
                        if "user" in val.lower() or "message" in val.lower() or "content" in val.lower():
                            snippet = val[:2000] if len(val) > 2000 else val
                            print(f"\n=== Match in key: {key[:100]} ===")
                            print(snippet)
                            print("...")
        except Exception as e:
            print(f"Error on table {tbl}: {e}")
        print()

    # Extract Ashlands Reborn initial conversation - composer c6121a55
    print("\n" + "=" * 60)
    print("ASHLANDS REBORN - INITIAL PROMPT EXTRACTION")
    print("=" * 60)
    composer_id = "c6121a55-8f1f-471c-9ad4-e38dc16a03f4"
    cur.execute(
        "SELECT key, value FROM cursorDiskKV WHERE key LIKE ? ORDER BY key",
        (f"bubbleId:{composer_id}:%",),
    )
    bubbles = []
    for key, val in cur.fetchall():
        try:
            data = json.loads(val)
            bubble_id = key.split(":")[-1]
            msg_type = data.get("type", 0)  # 1=user, 2=assistant
            created = data.get("createdAt", "")
            text = data.get("text", "") or ""
            rt = data.get("richText", "")
            if rt:
                extracted = extract_text_from_richtext(rt)
                if extracted:
                    text = extracted
            bubbles.append((created, msg_type, bubble_id, text))
        except (json.JSONDecodeError, TypeError) as e:
            pass
    bubbles.sort(key=lambda x: x[0])
    out_lines = []
    for created, msg_type, bid, text in bubbles[:10]:
        role = "USER" if msg_type == 1 else "ASSISTANT"
        raw = (text or "").replace("\n", " ")
        if raw:
            out_lines.append(f"\n[{created}] {role}:")
            out_lines.append(raw[:800])
            if len(raw) > 800:
                out_lines.append("...")
    result = "\n".join(out_lines)
    print(result)
    # Also save to file for safe Unicode handling
    with open(os.path.join(os.path.dirname(__file__), "..", "INITIAL_PROMPT_FOUND.txt"), "w", encoding="utf-8") as f:
        f.write("Initial prompt from Cursor chat (Ashlands Reborn, Feb 16 2026):\n")
        f.write("=" * 60 + "\n")
        for created, msg_type, bid, text in bubbles[:5]:
            role = "USER" if msg_type == 1 else "ASSISTANT"
            if text:
                f.write(f"\n[{created}] {role}:\n")
                f.write((text or "") + "\n")

    conn.close()
    print("\nDone.")

if __name__ == "__main__":
    main()
