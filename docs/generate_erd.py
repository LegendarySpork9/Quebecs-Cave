"""Generate docs/Database ERD.drawio from a table+FK definition.

Style mirrors LegendarySpork9/Hunter-Industries-API/Database ERD.drawio:
each table is a `shape=table` container with PK/FK indicator on the left,
column name + type rows below the title row.

Run:  python docs/generate_erd.py
Output: docs/Database ERD.drawio
"""

from __future__ import annotations
from dataclasses import dataclass, field
from html import escape
from pathlib import Path


@dataclass
class Column:
    key: str  # 'PK', 'FK', 'PK,FK', or ''
    name: str
    type_: str
    nullable: bool = False


@dataclass
class Table:
    name: str
    cols: list[Column]
    x: int
    y: int


@dataclass
class FK:
    from_table: str
    from_col: str
    to_table: str
    to_col: str


# --- Schema -----------------------------------------------------------------

T = []  # tables
F = []  # foreign keys

# Columns are (key, name, type, nullable)
def tbl(name, cols, x, y):
    T.append(Table(name, [Column(*c) for c in cols], x, y))

ROW_W = 280
COL_W = 320

# Row 1 — Reference / lookup
tbl("Role", [
    ("PK", "RoleId", "int", False),
    ("",   "Name",   "varchar(20)", False),
], 0, 0)

tbl("ErrorStatus", [
    ("PK", "ErrorStatusId", "int", False),
    ("",   "Name",          "varchar(30)", False),
], COL_W, 0)

tbl("SchemaVersion", [
    ("PK", "Id",         "int", False),
    ("",   "ScriptName", "nvarchar(255)", False),
    ("",   "Applied",    "datetime", False),
], COL_W * 2, 0)

# Row 2 — Identity
tbl("User", [
    ("PK", "UserId",          "int", False),
    ("",   "TwitchUserId",    "varchar(50)", False),
    ("",   "TwitchLogin",     "varchar(50)", False),
    ("",   "DisplayName",     "nvarchar(100)", False),
    ("",   "AvatarUrl",       "varchar(500)", True),
    ("",   "ThemePreference", "varchar(10)", True),
    ("",   "TimeZoneId",      "varchar(100)", True),
    ("",   "CreatedAt",       "datetimeoffset", False),
    ("",   "LastSeenAt",      "datetimeoffset", False),
], 0, 220)

tbl("Developer", [
    ("PK", "DeveloperId",   "int", False),
    ("",   "TwitchUserId",  "varchar(50)", False),
    ("",   "TwitchLogin",   "varchar(50)", False),
    ("FK", "AddedByUserId", "int", False),
    ("",   "AddedAt",       "datetimeoffset", False),
], COL_W, 220)
F.append(FK("Developer", "AddedByUserId", "User", "UserId"))

tbl("ModeratorCache", [
    ("PK", "ModeratorCacheId", "int", False),
    ("",   "TwitchUserId",     "varchar(50)", False),
    ("",   "TwitchLogin",      "varchar(50)", False),
    ("",   "RefreshedAt",      "datetimeoffset", False),
], COL_W * 2, 220)

tbl("TwitchToken", [
    ("PK", "TwitchTokenId", "int", False),
    ("FK", "UserId",        "int", False),
    ("",   "AccessToken",   "varbinary(max)", False),
    ("",   "RefreshToken",  "varbinary(max)", False),
    ("",   "ExpiresAt",     "datetimeoffset", False),
    ("",   "Scopes",        "varchar(500)", False),
], COL_W * 3, 220)
F.append(FK("TwitchToken", "UserId", "User", "UserId"))

tbl("LoginAttempt", [
    ("PK", "LoginAttemptId", "int", False),
    ("",   "TwitchUserId",   "varchar(50)", True),
    ("",   "IpHash",         "varbinary(32)", False),
    ("",   "SucceededAt",    "datetimeoffset", True),
    ("",   "FailureReason",  "varchar(200)", True),
    ("",   "AttemptedAt",    "datetimeoffset", False),
], COL_W * 4, 220)

# Row 3 — Content
tbl("Game", [
    ("PK", "GameId",       "int", False),
    ("",   "Name",         "nvarchar(150)", False),
    ("",   "Slug",         "varchar(150)", False),
    ("",   "IconUrl",      "varchar(500)", True),
    ("",   "TwitchGameId", "varchar(50)", True),
    ("",   "IsCustomIcon", "bit", False),
    ("",   "CreatedAt",    "datetimeoffset", False),
], 0, 540)

tbl("Stream", [
    ("PK",  "StreamId",        "int", False),
    ("",    "Title",           "nvarchar(300)", False),
    ("",    "Description",     "nvarchar(max)", True),
    ("FK",  "GameId",          "int", False),
    ("",    "StreamedAt",      "datetimeoffset", False),
    ("",    "DurationSeconds", "int", False),
    ("",    "VideoUrl",        "varchar(500)", False),
    ("",    "ThumbnailUrl",    "varchar(500)", True),
    ("",    "TwitchVodId",     "varchar(50)", True),
    ("",    "CreatedAt",       "datetimeoffset", False),
], COL_W, 540)
F.append(FK("Stream", "GameId", "Game", "GameId"))

tbl("Emoji", [
    ("PK", "EmojiId",   "int", False),
    ("",   "Code",      "varchar(50)", False),
    ("",   "Name",      "varchar(100)", False),
    ("",   "ImageUrl",  "varchar(500)", False),
    ("",   "IsActive",  "bit", False),
    ("",   "SortOrder", "int", False),
], COL_W * 2, 540)

tbl("Reaction", [
    ("PK", "ReactionId", "int", False),
    ("FK", "StreamId",   "int", False),
    ("FK", "UserId",     "int", False),
    ("FK", "EmojiId",    "int", False),
    ("",   "CreatedAt",  "datetimeoffset", False),
], COL_W * 3, 540)
F.append(FK("Reaction", "StreamId", "Stream", "StreamId"))
F.append(FK("Reaction", "UserId", "User", "UserId"))
F.append(FK("Reaction", "EmojiId", "Emoji", "EmojiId"))

tbl("StreamView", [
    ("PK", "StreamViewId", "int", False),
    ("FK", "StreamId",     "int", False),
    ("FK", "UserId",       "int", True),
    ("",   "IpHash",       "varbinary(32)", False),
    ("",   "ViewedAt",     "datetimeoffset", False),
], COL_W * 4, 540)
F.append(FK("StreamView", "StreamId", "Stream", "StreamId"))
F.append(FK("StreamView", "UserId", "User", "UserId"))

# Row 4 — Audit / observability
tbl("AuditHistory", [
    ("PK", "AuditHistoryId", "int", False),
    ("FK", "UserId",         "int", True),
    ("",   "Entity",         "varchar(50)", False),
    ("",   "EntityId",       "int", False),
    ("",   "Action",         "varchar(20)", False),
    ("",   "Diff",           "nvarchar(max)", True),
    ("",   "CreatedAt",      "datetimeoffset", False),
], 0, 880)
F.append(FK("AuditHistory", "UserId", "User", "UserId"))

tbl("Deletion", [
    ("PK", "DeletionId", "int", False),
    ("",   "Entity",    "varchar(50)", False),
    ("",   "EntityId",  "int", False),
    ("FK", "UserId",    "int", True),
    ("",   "DeletedAt", "datetimeoffset", False),
], COL_W, 880)
F.append(FK("Deletion", "UserId", "User", "UserId"))

tbl("ApiCallLog", [
    ("PK", "ApiCallLogId",   "bigint", False),
    ("",   "Method",         "varchar(10)", False),
    ("",   "Path",           "varchar(500)", False),
    ("",   "QueryString",    "varchar(2000)", True),
    ("",   "RequestBody",    "nvarchar(max)", True),
    ("",   "ResponseStatus", "int", False),
    ("",   "ResponseBody",   "nvarchar(max)", True),
    ("FK", "UserId",         "int", True),
    ("",   "IpHash",         "varbinary(32)", False),
    ("",   "ServiceKeyHash", "varbinary(32)", True),
    ("",   "DurationMs",     "int", False),
    ("",   "CalledAt",       "datetimeoffset", False),
    ("FK", "RelatedAuditId", "int", True),
], COL_W * 2, 880)
F.append(FK("ApiCallLog", "UserId", "User", "UserId"))
F.append(FK("ApiCallLog", "RelatedAuditId", "AuditHistory", "AuditHistoryId"))

tbl("DownloaderEvent", [
    ("PK", "DownloaderEventId", "bigint", False),
    ("",   "Stage",             "varchar(30)", False),
    ("",   "TwitchVodId",       "varchar(50)", True),
    ("",   "Success",           "bit", False),
    ("",   "DurationMs",        "int", True),
    ("",   "Payload",           "nvarchar(max)", True),
    ("",   "Message",           "nvarchar(1000)", True),
    ("",   "OccurredAt",        "datetimeoffset", False),
], COL_W * 3, 880)

tbl("WebsiteEvent", [
    ("PK", "WebsiteEventId", "bigint", False),
    ("",   "Action",         "varchar(50)", False),
    ("",   "Path",           "varchar(500)", True),
    ("FK", "UserId",         "int", True),
    ("",   "IpHash",         "varbinary(32)", False),
    ("",   "Detail",         "nvarchar(max)", True),
    ("",   "OccurredAt",     "datetimeoffset", False),
], COL_W * 4, 880)
F.append(FK("WebsiteEvent", "UserId", "User", "UserId"))

tbl("ErrorLog", [
    ("PK", "ErrorLogId",        "bigint", False),
    ("",   "Source",            "varchar(20)", False),
    ("",   "ExceptionType",     "varchar(200)", False),
    ("",   "Message",           "nvarchar(2000)", False),
    ("",   "StackTrace",        "nvarchar(max)", True),
    ("",   "Context",           "nvarchar(max)", True),
    ("FK", "StatusId",          "int", False),
    ("",   "GitHubIssueUrl",    "varchar(500)", True),
    ("FK", "AddressedByUserId", "int", True),
    ("",   "AddressedAt",       "datetimeoffset", True),
    ("",   "Notes",             "nvarchar(max)", True),
    ("",   "OccurredAt",        "datetimeoffset", False),
], COL_W * 5, 880)
F.append(FK("ErrorLog", "StatusId", "ErrorStatus", "ErrorStatusId"))
F.append(FK("ErrorLog", "AddressedByUserId", "User", "UserId"))


# --- Drawio XML emission ----------------------------------------------------

ROW_HEIGHT = 26
TITLE_HEIGHT = 30
TABLE_WIDTH = 260
KEY_COL_WIDTH = 38

cells = []
table_id_for = {}

cells.append('<mxCell id="0" />')
cells.append('<mxCell id="1" parent="0" />')

def add(s):
    cells.append(s)

cell_n = 100

def next_id():
    global cell_n
    cell_n += 1
    return f"c{cell_n}"


for t in T:
    height = TITLE_HEIGHT + len(t.cols) * ROW_HEIGHT
    table_id = next_id()
    table_id_for[t.name] = table_id

    add(f'<mxCell id="{table_id}" value="{escape(t.name)}" '
        f'style="shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;'
        f'fixedRows=1;rowLines=0;fontStyle=1;align=center;resizeLast=1;html=1;fillColor=#fff2cc;strokeColor=#d49a3a;" '
        f'vertex="1" parent="1">'
        f'<mxGeometry x="{t.x}" y="{t.y}" width="{TABLE_WIDTH}" height="{height}" as="geometry"/>'
        f'</mxCell>')

    for i, c in enumerate(t.cols):
        row_id = next_id()
        key_id = next_id()
        name_id = next_id()

        add(f'<mxCell id="{row_id}" '
            f'style="shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;fillColor=none;collapsible=0;dropTarget=0;points=[[0,0.5],[1,0.5]];portConstraint=eastwest;top=0;left=0;right=0;bottom=1;" '
            f'vertex="1" parent="{table_id}">'
            f'<mxGeometry y="{30 + i*ROW_HEIGHT}" width="{TABLE_WIDTH}" height="{ROW_HEIGHT}" as="geometry"/>'
            f'</mxCell>')

        add(f'<mxCell id="{key_id}" value="{escape(c.key)}" '
            f'style="shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;fontStyle=1;overflow=hidden;whiteSpace=wrap;html=1;" '
            f'vertex="1" parent="{row_id}">'
            f'<mxGeometry width="{KEY_COL_WIDTH}" height="{ROW_HEIGHT}" as="geometry"/>'
            f'</mxCell>')

        label = f"{c.name} : {c.type_}{' NULL' if c.nullable else ''}"
        font_style = 4 if c.key.startswith('PK') else 0  # underline PKs
        add(f'<mxCell id="{name_id}" value="{escape(label)}" '
            f'style="shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;align=left;spacingLeft=6;fontStyle={font_style};overflow=hidden;whiteSpace=wrap;html=1;" '
            f'vertex="1" parent="{row_id}">'
            f'<mxGeometry x="{KEY_COL_WIDTH}" width="{TABLE_WIDTH-KEY_COL_WIDTH}" height="{ROW_HEIGHT}" as="geometry"/>'
            f'</mxCell>')

# Foreign-key arrows (table-to-table; not pointing at specific rows, since
# row IDs are dynamic and arrows look cleaner table-to-table anyway)
for fk in F:
    src = table_id_for.get(fk.from_table)
    dst = table_id_for.get(fk.to_table)
    if not src or not dst:
        continue
    edge_id = next_id()
    add(f'<mxCell id="{edge_id}" value="" '
        f'style="edgeStyle=entityRelationEdgeStyle;fontSize=12;html=1;endArrow=ERmany;startArrow=ERone;rounded=0;exitX=0;exitY=0.5;entryX=1;entryY=0.5;" '
        f'edge="1" parent="1" source="{src}" target="{dst}">'
        f'<mxGeometry relative="1" as="geometry"/>'
        f'</mxCell>')


xml_inner = "\n        ".join(cells)
xml = f'''<?xml version="1.0" encoding="UTF-8"?>
<mxfile host="app.diagrams.net">
  <diagram name="Quebec's Cave Schema" id="quebecs-cave-erd">
    <mxGraphModel dx="2000" dy="1500" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="1654" pageHeight="2339" math="0" shadow="0">
      <root>
        {xml_inner}
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
'''

out = Path(__file__).parent / "Database ERD.drawio"
out.write_text(xml, encoding="utf-8")
print(f"Wrote {out} ({len(xml)} bytes, {len(T)} tables, {len(F)} FKs)")
