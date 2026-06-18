#!/usr/bin/env python3
"""
Build the PlantData autofill file from SSOT.xlsx (sheet 2, "New Version").

Reads the spreadsheet with the Python standard library only (an .xlsx is a zip of
XML — no openpyxl needed) and writes `_ssot_fill.txt` at the project root: a
delimiter-separated file the Unity side (see Assets/_Claude/SSOT_Autofill.md)
reads back to fill every PlantData asset's poem + context text.

    python Tools/ssot_build.py

Layout of sheet 2 (columns B..M = the 12 plants):
    row 2  Poem Title      row 9..14  Context parts (one non-empty cell = one part)
    row 3  Author          (rows 5/6/7/8 = Type/Age/Location/Translator, unused)
    row 4  Poem body
Poem text is composed as:  Title (unless "unnamed") + blank line + body + blank line + "— Author".

Output format (LF only):
    records separated by 0x1E, fields by 0x1F.
    field[0] = asset path, field[1] = poem, field[2..] = context parts.
"""
import os, zipfile, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XLSX = os.path.join(ROOT, "Assets", "_Projects", "_Resources", "SSOT.xlsx")
OUT  = os.path.join(ROOT, "_ssot_fill.txt")
PLANTINFO = "Assets/_Projects/_Resources/_PlantInfo/"

# Spreadsheet column -> PlantData asset file name (note the project's spellings).
COLS = {'B': 'Poppy', 'C': 'Crocus', 'D': 'Bamboo', 'E': 'Date_Palm',
        'F': 'Rhododentron', 'G': 'Hemp', 'H': 'Hibiscus', 'I': 'Fern',
        'J': 'Narcissus', 'K': 'Lavender', 'L': 'Pear_Tree', 'M': 'Fig_Tree'}

M = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
def q(t): return '{%s}%s' % (M, t)


def col_letters(ref):  # "AB12" -> "AB"
    return ''.join(c for c in ref if c.isalpha())


def load_grid():
    z = zipfile.ZipFile(XLSX)
    shared = [''.join(t.text or '' for t in si.iter(q('t')))
              for si in ET.fromstring(z.read('xl/sharedStrings.xml')).findall(q('si'))]

    # Resolve the sheet named "New Version" (fall back to the 2nd sheet).
    wb = ET.fromstring(z.read('xl/workbook.xml'))
    rels = {rel.get('Id'): rel.get('Target')
            for rel in ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))}
    target = None
    sheets = wb.find(q('sheets'))
    for i, s in enumerate(sheets):
        if s.get('name') == 'New Version':
            target = rels[s.get('{%s}id' % R)]
    if target is None:
        target = rels[list(sheets)[1].get('{%s}id' % R)]
    if not target.startswith('xl/'):
        target = 'xl/' + target

    grid = {}
    sh = ET.fromstring(z.read(target))
    for row in sh.iter(q('row')):
        rn = int(row.get('r'))
        for c in row.findall(q('c')):
            col = col_letters(c.get('r')); t = c.get('t'); v = c.find(q('v'))
            if t == 's':
                val = shared[int(v.text)] if v is not None else ''
            elif t == 'inlineStr':
                isn = c.find(q('is'))
                val = ''.join(x.text or '' for x in isn.iter(q('t'))) if isn is not None else ''
            else:
                val = v.text if v is not None else ''
            grid[(col, rn)] = val
    return grid


def main():
    grid = load_grid()
    def cell(col, row): return (grid.get((col, row)) or '').strip()

    records = []
    for col, asset in COLS.items():
        title, author, body = cell(col, 2), cell(col, 3), cell(col, 4)
        poem = ''
        if title and title.lower() != 'unnamed':
            poem += title + '\n\n'
        poem += body
        if author:
            poem += '\n\n— ' + author
        poem = poem.strip()
        contexts = [cell(col, r) for r in range(9, 15) if cell(col, r)]
        fields = [PLANTINFO + asset + '.asset', poem] + contexts
        assert all('\x1e' not in f and '\x1f' not in f for f in fields), asset
        records.append('\x1f'.join(fields))
        print(f"{asset:<16} poem {len(poem):>4} chars, {len(contexts)} contexts")

    # newline='' keeps pure LF (no Windows \r\n translation).
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write('\x1e'.join(records))
    print(f"\nwrote {OUT}  ({len(records)} records)")


if __name__ == '__main__':
    main()
