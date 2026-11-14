import pathlib,json,sys,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
g=pathlib.Path(sys.argv[1] if len(sys.argv)>1 else r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket')  # game directory, read-only
part=json.loads((g/'Sprocket_Data/StreamingAssets/Parts/appliquePlatePart.json').read_text())
part.update(v='0.3',guid='a22720e6-c10c-47d7-96a0-00a03b9d8b20',name='referenceImage',date='0000.01.01',tags=['decoration','crewEquipment','referenceImage'],order=1000,iconOffset=[0,0],iconRotation=[0,0,0],iconScale=1)
component=part['components'][0];component['fileID']='referenceImageModel'
component['info'].update(v='0.2',mass=0,damageable=False,selectable=True,placementSurface=False)
component['info'].pop('associatedComponentFileID',None)
component['info'].pop('durableMaterialID',None)
part['transform'].update(minScale=[.1,.1,.1],maxScale=[6,6,6],scaling=True,placementMode='upright',autoRotate=True)
for name in ('parts','localization'):(root/'src'/name).mkdir(exist_ok=True)
(root/'src/parts/referenceImagePart.json').write_text(json.dumps(part,indent=2)+'\n',encoding='utf-8')
xml='<?xml version="1.0" encoding="utf-16"?>\n<Part>\n  <name>Reference Image</name>\n  <description>A reference picture board. Move, rotate and scale with the native tools. Images are assigned for this game session only. Select exactly one board to choose a PNG or JPG, paste an image or mirror it. Configure Reference Images / Panel and Reference Images / Paste in vanilla Settings / keybinds.</description>\n</Part>\n'
(root/'src/localization/referenceImage.xml').write_text(xml,encoding='utf-16')
print('Prepared standalone model-only part and UTF-16 localization.')
