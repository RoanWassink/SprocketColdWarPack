"""Fail-closed source inventory + Cecil inheritance/signatures + native RVA sharing audit.

Run via check.ps1 (which refreshes the read-only Cecil export first):
    py -I icf_audit.py <all-native-methods.json> <icf-metadata.json> <icf-targets report to write>
<all-native-methods.json> is the native method dump (name, RVA, length) of the game's GameAssembly.dll; it is only read.
tests\baseline-0.3.0.json is the frozen reference source of the runtime scope (see CHANGELOG 0.3.1).
The REQUIRED set is only a coverage floor; it never supplies audit targets.
New registration/resolver syntax requires explicit parser support, not an exception.
"""
import hashlib
import json
import pathlib
import re
import sys
from collections import defaultdict

PROJECT = pathlib.Path(__file__).resolve().parents[1]
EXCEPTIONS = {}  # No shared native addresses are permitted.
REQUIRED = {
    'VehicleComponent.ReleaseInternal',
    *('TurretRing.' + x for x in ['CreateNewBlueprint', 'InitiateMotor', 'EnableBehaviour', 'Build']),
    *('TraverseMotor.' + x for x in ['EnableBehaviour', 'DisableBehaviour']),
    *('TurretBehaviour.' + x for x in ['MoveToTarget', 'Apply', 'ResetRotation', 'Dispose']),
    'TurretBasket.SetVisible',
    *('VehicleBattleOverlayController.' + x for x in ['ToggleOverlay', 'SetOverlayActive']),
    *('PlateStructureDesigner.' + x for x in ['GetSelection', 'Release']),
    *('LayingDrive.' + x for x in ['EnableBehaviour', 'Build', 'ValidateTrunnionDistance']),
    'Mantlet.Build', 'VehicleObjectModel.SetVisible', 'VehicleObjectModel.Build', 'PartDefinitionCardFactory.CreateCard',
}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def strip_comments(text):
    # Preserve literals so apparent comments within strings are not removed.
    text = text.replace('\r\n', '\n')
    return re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/',
                  lambda m: m[0] if m[0].startswith('"') else ' ' * len(m[0]), text)


def closing(text, start):
    pairs = {'(': ')', '[': ']', '{': '}'}
    stack = []
    for m in re.finditer(r'"(?:\\.|[^"\\])*"|[()[\]{}]', text[start:]):
        c = m[0]
        if c.startswith('"'):
            continue
        if c in pairs:
            stack.append(pairs[c])
        else:
            require(stack and c == stack.pop(), 'unbalanced C# expression')
            if not stack:
                return start + m.end()
    raise ValueError('unterminated C# expression')


def arguments(text):
    # Split only top-level commas, including typeof arrays and nested nameof calls.
    result = []; last = 0; i = 0
    while i < len(text):
        if text[i] in '([{':
            i = closing(text, i)
        elif text[i] == '"':
            match = re.match(r'"(?:\\.|[^"\\])*"', text[i:])
            require(match is not None, 'unterminated string')
            i += match.end()
        elif text[i] == ',':
            result.append(text[last:i].strip()); i += 1; last = i
        else:
            i += 1
    result.append(text[last:].strip())
    return result


def calls(text, pattern):
    for m in re.finditer(pattern + r'\s*\(', text):
        start = text.index('(', m.start())
        yield m, arguments(text[start + 1:closing(text, start) - 1])


def type_expr(value):
    m = re.fullmatch(r'typeof\(\s*([\w.]+)\s*\)', value)
    require(m is not None, 'unresolved declaring/parameter type: ' + value)
    return m[1]


def method_expr(value):
    m = re.fullmatch(r'"(\w+)"|nameof\(\s*[\w.]+\.(\w+)\s*\)', value)
    require(m is not None, 'unresolved method name: ' + value)
    return m[1] or m[2]


def parameters(value):
    if value == 'null':
        return None
    if value == 'Type.EmptyTypes':
        return []
    m = re.fullmatch(r'new\s*(?:\[\]|Type\[\])\s*\{(.*)\}', value, re.S)
    require(m is not None, 'unresolved overload selector: ' + value)
    return [type_expr(x) for x in arguments(m[1])] if m[1].strip() else []


def helper_span(text):
    m = re.search(r'private\s+static\s+void\s+Patch\s*\(', text)
    if not m:
        return None
    start = text.index('{', closing(text, text.index('(', m.start())))
    return m.start(), closing(text, start)


def extract(sources, baseline):
    targets = []
    for file, raw in sources.items():
        text = strip_comments(raw)
        span = helper_span(text)
        if span:
            require(file in ('Plugin.cs', 'HeavyTrunnion.cs'), 'unreviewed Patch helper: ' + file)
            old = strip_comments(baseline['src\\' + file]['text'])
            old_span = helper_span(old)
            require(text[slice(*span)] == old[slice(*old_span)], 'dynamic Patch helper changed: ' + file)
        inside = lambda pos: span is not None and span[0] <= pos < span[1]
        lookups = {}
        for m, args in calls(text, r'\bAccessTools\.(?:Method|DeclaredMethod)'):
            if inside(m.start()):
                continue  # Exact helper bodies above are pinned, including their variable resolvers.
            require(2 <= len(args) <= 3, file + ': unsupported AccessTools target')
            target = (type_expr(args[0]), method_expr(args[1]), parameters(args[2]) if len(args) == 3 else None,
                      'DeclaredMethod' in m[0])
            targets.append((file, *target))
            assignment = re.search(r'\b(?:var|MethodInfo\??)\s+(\w+)\s*=\s*$', text[:m.start()])
            if assignment:
                lookups[assignment[1]] = target
        for m, args in calls(text, r'(?<![\w.])Patch'):
            if inside(m.start()):
                continue
            if args[0] == 'harmony':
                require(file == 'Plugin.cs' and len(args) == 7, file + ': unknown Patch helper call')
                targets.append((file, type_expr(args[1]), method_expr(args[2]), parameters(args[3]), False))
            else:
                require(file == 'HeavyTrunnion.cs' and len(args) == 5, file + ': unknown Patch helper call')
                targets.append((file, type_expr(args[0]), method_expr(args[1]), None, True))
        for m, args in calls(text, r'\bHarmonyPatch'):
            require(2 <= len(args) <= 3, file + ': unsupported HarmonyPatch attribute')
            targets.append((file, type_expr(args[0]), method_expr(args[1]), parameters(args[2]) if len(args) == 3 else None, False))
        # Bare [HarmonyPatch] is a marker only; reject every other attribute form we do not parse.
        for m in re.finditer(r'\bHarmonyPatch\b', text):
            tail = text[m.end():].lstrip()
            require(tail.startswith(('(', ']')), file + ': unsupported HarmonyPatch syntax')
        for m, args in calls(text, r'\b\w+\s*[!?]?\s*\.\s*Patch'):
            if inside(m.start()):
                continue
            first = args[0]
            require(first.startswith('AccessTools.Method(') or first.startswith('AccessTools.DeclaredMethod(')
                    or first in lookups, file + ': unresolved Harmony registration: ' + first)
        for m, args in calls(text, r'\b\w+\.PatchAll'):
            require(args == ['typeof(PivotHousingVisuals)'] and file == 'Plugin.cs',
                    file + ': unreviewed PatchAll scope')
        # Fail on APIs/attributes that would evade the supported registration grammar.
        require(not re.search(r'\b(?:TargetMethod|TargetMethods|PatchCategory|PatchAllUncategorized|HarmonyTargetMethod|HarmonyTargetMethods|HarmonyReversePatch|HarmonyDelegate|HarmonyPatchAll|PatchProcessor|CreateProcessor|CreateClassProcessor|PatchClassProcessor)\b', text),
                file + ': unreviewed dynamic/alternate Harmony target')
        require(not re.search(r'\b(?:Patch|Method|DeclaredMethod|PatchAll)\s*<|\bHarmonyPatchAttribute\b', text),
                file + ': unreviewed generic/alternate target syntax')
    return targets


ALIASES = {'float': 'System.Single', 'bool': 'System.Boolean', 'int': 'System.Int32'}


def normalize(value):
    return value.replace('Il2CppSystem.', 'System.')


def resolver(metadata, primary):
    types = {t['name']: t for t in metadata['interopTypes']}
    native = {r['Name']: r for r in primary}
    full_native = {r['Name']: r for r in metadata['native']}
    shared = defaultdict(set)
    for r in primary + metadata['native']:
        shared[int(r['RVA'], 16)].add(r['Name'])

    def fullname(name):
        if name in ALIASES:
            return ALIASES[name]
        if name in types:
            return name
        choices = [n for n in types if n.rsplit('.', 1)[-1] == name]
        require(len(choices) == 1, 'ambiguous/unknown type ' + name)
        return choices[0]

    def resolve(target):
        file, requested, method, params, declared = target
        name = fullname(requested)
        expected = [fullname(p) for p in params] if params is not None else None
        while name in types:
            t = types[name]
            choices = [m for m in t['methods'] if m['name'] == method and
                       (expected is None or [normalize(p) for p in m['parameters']] == expected)]
            require(len(choices) <= 1, f'ambiguous overload {requested}.{method}: {choices}')
            if choices:
                signature = normalize(choices[0]['signature'])
                require(signature in native, 'target missing from primary dump: ' + signature)
                r = native[signature]
                require(signature in full_native and full_native[signature]['RVA'] == r['RVA'] and
                        full_native[signature]['Length'] == r['Length'], 'dump/Cecil address mismatch: ' + signature)
                address = int(r['RVA'], 16)
                others = shared[address] - {signature}
                require(not others or signature in EXCEPTIONS, f'SHARED RVA {r["RVA"]}: {signature}; other methods={len(others)}')
                return {'source': file, 'requested': requested + '.' + method, 'signature': signature,
                        'rva': r['RVA'], 'length': r['Length'], 'otherMethods': sorted(others), 'declaringType': name}
            if declared:
                break
            name = t['base']
        raise ValueError('unresolved target: ' + requested + '.' + method)

    return resolve


def rejects(action, label):
    try:
        action()
    except ValueError:
        print('PASS negative control:', label)
        return
    raise ValueError('negative control was incorrectly accepted: ' + label)


def main():
    sources = {str(p.relative_to(PROJECT / 'src')): p.read_text(encoding='utf-8-sig')
               for p in sorted((PROJECT / 'src').rglob('*.cs'))
               if not {'bin', 'obj'} & set(p.relative_to(PROJECT / 'src').parts)}
    require(len(sys.argv) == 4, 'usage: icf_audit.py <all-native-methods.json> <icf-metadata.json> <icf-targets report to write>')
    dump_path, metadata_path, report_path = (pathlib.Path(a) for a in sys.argv[1:4])
    baseline = json.loads((PROJECT / 'tests/baseline-0.3.0.json').read_text(encoding='utf-8'))
    metadata = json.loads(metadata_path.read_text(encoding='utf-8-sig'))
    primary = json.loads(dump_path.read_text(encoding='utf-8-sig'))
    require(metadata['gameSha256'] == '3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B',
            'GameAssembly changed; repeat native research before trusting the address dumps')
    # Enforce the narrow runtime scope of 0.3.1, including the whole cleanup body and fallbacks.
    for path, record in baseline.items():
        if not path.startswith('src\\'):
            continue
        current = PROJECT.joinpath(*path.split('\\'))
        if current.name == 'Plugin.cs':
            expected = record['text'].replace('public const string Version = "0.3.0";',
                                              'public const string Version = "0.3.1";')
            expected = expected.replace('        Patch(harmony, typeof(VehicleComponent), "Release", Type.EmptyTypes,',
                '        // Release() is an ICF-folded empty stub. Its unique lifecycle caller runs\n'
                '        // before virtual Release and resource disposal; keep this cleanup a prefix.\n'
                '        Patch(harmony, typeof(VehicleComponent), "ReleaseInternal", Type.EmptyTypes,')
            require(current.read_text(encoding='utf-8-sig').replace('\r\n', '\n') == expected.replace('\r\n', '\n'),
                    'Plugin.cs changed beyond version and the release target/comment')
        elif current.name == 'SprocketOscillatingTurret.csproj':
            expected = record['text'].replace('<Version>0.3.0</Version>', '<Version>0.3.1</Version>')
            require(current.read_text(encoding='utf-8-sig') == expected.replace('\r\n', '\n'), 'csproj changed beyond version')
        else:
            require(hashlib.sha256(current.read_bytes()).hexdigest() == record['sha256'], 'out-of-scope source/asset edit: ' + path)
    print('PASS runtime scope: cleanup body, identity, signal, ring path, Heavy Trunnion, assets and logging default unchanged')
    targets = extract(sources, baseline)
    resolve = resolver(metadata, primary)
    audit = [resolve(t) for t in targets]
    found = {a['requested'] for a in audit}
    require(REQUIRED <= found, 'missing required source targets: ' + str(REQUIRED - found))
    require(len(found) >= 23, 'fewer than 23 targets')
    require(len({a['signature'] for a in audit}) == len(found), 'two source targets unexpectedly alias the same native method')
    # Check the actual regression, fail-closed discovery, overloads and inheritance in this same round.
    rejects(lambda: resolve(('negative', 'VehicleComponent', 'Release', [], False)), 'old Release is shared')
    changed = dict(sources); changed['Plugin.cs'] += '\nvoid Added() { harmony.Patch(dynamicTarget); }'
    rejects(lambda: extract(changed, baseline), 'unknown registration')
    changed = dict(sources); changed['Plugin.cs'] += '\nvoid Added() { Patch(harmony, typeof(VehicleComponent), "NoSuchMethod", Type.EmptyTypes, null, null, failures); }'
    rejects(lambda: [resolve(t) for t in extract(changed, baseline)], 'new unresolved source target')
    changed = dict(sources); changed['Plugin.cs'] += '\n[HarmonyPatch(typeof(TurretRing), "Release")] class Added {}'
    rejects(lambda: [resolve(t) for t in extract(changed, baseline)], 'new inherited shared attribute target')
    inherited = resolve(('control', 'TurretRing', 'ReleaseInternal', [], False))
    require(inherited['declaringType'] == 'Sprocket.Vehicles.VehicleComponent', 'inherited lookup used wrong native owner')
    rejects(lambda: resolve(('control', 'TurretRing', 'ReleaseInternal', [], True)), 'DeclaredMethod does not inherit')
    rejects(lambda: resolve(('control', 'TurretBasket', 'Release', None, False)), 'ambiguous overload')
    # A new safe source target must be discovered without editing REQUIRED or the parser.
    changed = dict(sources); changed['Plugin.cs'] += '\nvoid Added() { harmony.Patch(AccessTools.Method(typeof(TraverseMotor), "Build")); }'
    added = [resolve(t) for t in extract(changed, baseline)]
    require('TraverseMotor.Build' in {a['requested'] for a in added}, 'new literal target was silently ignored')
    print('PASS inheritance, declared-only lookup and new safe-target discovery controls')
    distinct = {a['signature']: a for a in audit}
    rows = sorted(distinct.values(), key=lambda a: a['signature'])
    report = {'gameSha256': metadata['gameSha256'], 'primaryDump': dump_path.name,
              'primaryDumpSha256': hashlib.sha256(dump_path.read_bytes()).hexdigest().upper(),
              'annotations': len(audit), 'targets': len(rows), 'unique': sum(not a['otherMethods'] for a in rows),
              'exceptions': EXCEPTIONS, 'methods': rows}
    report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    for a in rows:
        print(f'{a["rva"]:12} {a["length"]:8} {a["signature"]} [{a["source"]}]')
    print(f'PASS ICF: {len(audit)} source annotations; {len(rows)} targets; {report["unique"]} unique; {len(EXCEPTIONS)} exceptions; '
          f'{len(primary)} primary and {len(metadata["native"])} cross-assembly records')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError) as error:
        print('FAIL ICF:', error, file=sys.stderr)
        sys.exit(1)
