from pathlib import Path
from zipfile import ZipFile
import re
import xml.etree.ElementTree as ET

source = Path(r"c:\Users\ztan\OneDrive - SmithGroup Companies Inc\Documents\GitHub\mcp-servers-for-revit\IT evaluation\MCP_Security_Review_mcp-servers-for-revit.docx")
out_path = source.with_suffix('.md')

ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
paragraphs = []
with ZipFile(source) as zf:
    root = ET.fromstring(zf.read('word/document.xml'))
    for p in root.findall('.//w:p', ns):
        text = ''.join(t.text for t in p.findall('.//w:t', ns) if t.text)
        text = re.sub(r'\s+', ' ', text).strip()
        if text:
            paragraphs.append(text)

if not paragraphs:
    paragraphs = ['MCP Security Review']

# Keep the first paragraph as a title, then add the rest as body text.
content = '# ' + paragraphs[0]
if len(paragraphs) > 1:
    content += '\n\n' + '\n\n'.join(paragraphs[1:])

out_path.write_text(content, encoding='utf-8')
print(f'WROTE {out_path}')
print(f'PARAGRAPH_COUNT {len(paragraphs)}')
print('---PREVIEW---')
print(content[:1200])
