# SSOT Autofill — filling PlantData text from the spreadsheet

How to (re)fill every `PlantData` asset's **poem** and **context** text from
`Assets/_Projects/_Resources/SSOT.xlsx`. Re-run this whenever the spreadsheet is updated.

## Source layout

Sheet **2 ("New Version")**. Columns **B–M** are the 12 plants; rows are fields:

| Row | Field | Used? |
|----|----|----|
| 1 | Plant name | label only |
| 2 | Poem Title | ✓ poem |
| 3 | Author | ✓ poem |
| 4 | Poem body | ✓ poem |
| 5–8 | Type / Age / Location / Translator | not imported (yet) |
| 9–14 | Context parts | ✓ — **each non-empty cell = one context part** |

**Column → asset** (mind the project's spellings):

| Sheet | Asset (`_Resources/_PlantInfo/…asset`) |
|---|---|
| Common Poppy | `Poppy` |
| Rhododendron | `Rhododentron` |
| Date Palm / Pear Tree / Fig Tree | `Date_Palm` / `Pear_Tree` / `Fig_Tree` |
| Crocus, Bamboo, Hemp, Hibiscus, Fern, Narcissus, Lavender | same name |

**Poem composition:** `Title` (skipped if blank or "unnamed") + blank line + `body` + blank line + `— Author` (if present).

## Run it — two steps

### 1. Build the fill file (Python, stdlib only — no openpyxl)

```
python Tools/ssot_build.py
```

Reads the xlsx and writes `_ssot_fill.txt` at the project root: records separated by `0x1E`,
fields by `0x1F`, `field[0]`=asset path, `field[1]`=poem, `field[2..]`=context parts. LF only.
Prints a per-plant char/context summary — eyeball it before applying.

### 2. Apply to the assets (unity-mcp `RunCommand`)

Ask Claude to "re-apply the SSOT" — it runs this in Unity. The script reads `_ssot_fill.txt`, sets
`poem.text`, and **resizes `contextInfos` to the cell count** (existing entries keep their
`environmentPainting`; the list is grown/truncated to match), then `SaveAssets()`:

```csharp
using System.IO; using System.Text; using System.Collections.Generic;
using UnityEngine; using UnityEditor; using Plants;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_ssot_fill.txt"));
        if (!File.Exists(path)) { result.LogError("Fill file not found: " + path); return; }
        string raw = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace("\r", "\n");

        foreach (var rec in raw.Split(''))            // 0x1E record sep
        {
            var f = rec.Split('');                    // 0x1F field sep
            if (f.Length < 2) { result.LogError("Bad record"); continue; }
            var pd = AssetDatabase.LoadAssetAtPath<PlantData>(f[0]);
            if (pd == null) { result.LogError("Missing asset: " + f[0]); continue; }

            if (pd.poem == null) pd.poem = new PlantLabelContent();
            pd.poem.text = f[1];

            int n = f.Length - 2;
            if (pd.contextInfos == null) pd.contextInfos = new List<PlantLabelContent>();
            while (pd.contextInfos.Count < n) pd.contextInfos.Add(new PlantLabelContent());
            while (pd.contextInfos.Count > n) pd.contextInfos.RemoveAt(pd.contextInfos.Count - 1);
            for (int i = 0; i < n; i++)
            {
                if (pd.contextInfos[i] == null) pd.contextInfos[i] = new PlantLabelContent();
                pd.contextInfos[i].text = f[i + 2];
            }
            EditorUtility.SetDirty(pd);
        }
        AssetDatabase.SaveAssets();
        result.Log("SSOT applied.");
    }
}
```

Then delete the temp file: `rm _ssot_fill.txt`.

## Gotchas (learned the hard way)

- **Don't use `JsonUtility`** to ferry the data in — it silently returns null on this content
  (unicode + newlines). The `0x1E`/`0x1F` delimited file sidesteps any parser.
- **Raw control-char literals get stripped** in the RunCommand transit. Write them as `''` /
  `''` in the C# (the Unity-side auto-fixer also normalizes them).
- **Line endings:** `ssot_build.py` writes LF; the C# also normalizes `\r\n`→`\n` so TMP never gets a
  stray `\r` glyph.
- **More context than slots:** a plant can now have more context parts than its `PlantInfos.prefab`
  has context-label slots/roots; `PlantInfo.SetData` only shows up to `contextLabels.Count`. The data
  is stored regardless — add slots in `PlantInfos` to surface the extras.

## Related

- `_Claude/TmpLabels.md` — the sprite→TMP label migration this text feeds.
