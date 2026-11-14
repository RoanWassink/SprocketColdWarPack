"""Writes localization/emitterPart.xml (UTF-16 LE with BOM, native <Part> root, lowercase text)."""
from pathlib import Path
R = Path(__file__).resolve().parents[1]
text = ('<?xml version="1.0" encoding="utf-16"?>\n'
        '<Part><name>emitter</name><description>ejects a casing, fire or smoke; cosmetic only. '
        'connect it in the i/o flow.</description></Part>\n')
(R / 'localization/emitterPart.xml').write_text(text, encoding='utf-16')
print('wrote localization/emitterPart.xml')
