import re, sys
from pathlib import Path
def facets(path):
    data=Path(path).read_text()
    return sorted(' '.join(x.split()) for x in re.findall(r'facet normal.*?endfacet',data,re.S))
a,b=map(facets,sys.argv[1:])
if a != b: raise SystemExit('Re-export facets differ')
print('Re-export facets match:',len(a))
