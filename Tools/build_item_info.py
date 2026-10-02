"""Gera PKHeX.Modern/Assets/item-info.json e text-info.json a partir dos dados do AllGenWiki (projeto HoennKantoWiki).

Uso: python Tools/build_item_info.py [caminho do HoennKantoWiki]

Junta, por item e por geracao:
- descricao em portugues (data/genN/item-descriptions.json);
- como obter itens-chave (data/genN/key-items.json);
- onde encontrar itens de evolucao por versao (data/genN/item-locations.json).

E, em text-info.json, a descricao em portugues de golpes e habilidades por geracao
(data/genN/i18n/pt.json), guardando o texto so nas geracoes em que ele muda.

A chave e o nome em ingles so com letras e numeros, minusculo ("King's Rock" -> "kingsrock"),
que e como o app procura pelo nome do item do PKHeX.
"""
import glob
import json
import os
import re
import sys

wiki = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', 'HoennKantoWiki')
out = os.path.join(os.path.dirname(__file__), '..', 'PKHeX.Modern', 'Assets', 'item-info.json')

GROUPS = {
    'black-white': 'Black/White', 'black-2-white-2': 'Black 2/White 2', 'x-y': 'X/Y',
    'omega-ruby-alpha-sapphire': 'Omega Ruby/Alpha Sapphire', 'sun-moon': 'Sun/Moon',
    'ultra-sun-ultra-moon': 'Ultra Sun/Ultra Moon', 'sword-shield': 'Sword/Shield',
    'brilliant-diamond-shining-pearl': 'Brilliant Diamond/Shining Pearl', 'legends-arceus': 'Legends: Arceus',
    'scarlet-violet': 'Scarlet/Violet', 'lets-go-pikachu-lets-go-eevee': "Let's Go",
}


def key(name):
    return re.sub(r'[^a-z0-9]', '', name.lower())


def load(path):
    with open(path, encoding='utf-8') as f:
        return json.load(f)


items = {}


def entry(name):
    return items.setdefault(key(name), {'name': name, 'desc': {}, 'where': {}})


for folder in sorted(glob.glob(os.path.join(wiki, 'data', 'gen*'))):
    m = re.search(r'gen(\d+)$', folder)
    if not m:
        continue
    gen = m.group(1)

    path = os.path.join(folder, 'item-descriptions.json')
    if os.path.exists(path):
        for slug, d in load(path).items():
            if slug.startswith('_') or not isinstance(d, dict) or not d.get('pt'):
                continue
            entry(d.get('name') or slug.replace('-', ' '))['desc'][gen] = d['pt'].strip()

    path = os.path.join(folder, 'key-items.json')
    if os.path.exists(path):
        for region, cats in load(path).items():
            for cat in cats if isinstance(cats, list) else []:
                for it in cat.get('items', []):
                    if it.get('name') and it.get('desc'):
                        entry(it['name'])['where'].setdefault(gen, []).append(f"{region.title()}: {it['desc'].strip()}")

    path = os.path.join(folder, 'item-locations.json')
    if os.path.exists(path):
        for slug, d in load(path).items():
            if slug.startswith('_') or not isinstance(d, dict):
                continue
            e = entry(slug.replace('-', ' ').title())
            for group, where in d.items():
                if isinstance(where, str) and where.strip():
                    e['where'].setdefault(gen, []).append(f"{GROUPS.get(group, group)}: {where.strip()}")

os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', encoding='utf-8') as f:
    json.dump(dict(sorted(items.items())), f, ensure_ascii=False, separators=(',', ':'))
print(f'{len(items)} itens -> {os.path.normpath(out)} ({os.path.getsize(out) // 1024} KB)')

# Golpes e habilidades: {"moves": {chave: {geracao: texto}}, "abilities": {...}}
texts = {'moves': {}, 'abilities': {}}
folders = sorted(glob.glob(os.path.join(wiki, 'data', 'gen*')), key=lambda f: int(re.search(r'gen(\d+)$', f).group(1)) if re.search(r'gen(\d+)$', f) else 0)
for folder in folders:
    m = re.search(r'gen(\d+)$', folder)
    path = os.path.join(folder, 'i18n', 'pt.json')
    if not m or not os.path.exists(path):
        continue
    gen = m.group(1)
    pt = load(path)
    for kind in texts:
        for slug, text in (pt.get(kind) or {}).items():
            if not isinstance(text, str) or not text.strip():
                continue
            byGen = texts[kind].setdefault(key(slug), {})
            last = byGen[max(byGen, key=int)] if byGen else None
            if text.strip() != last:
                byGen[gen] = text.strip()

out2 = os.path.join(os.path.dirname(out), 'text-info.json')
with open(out2, 'w', encoding='utf-8') as f:
    json.dump({k: dict(sorted(v.items())) for k, v in texts.items()}, f, ensure_ascii=False, separators=(',', ':'))
print(f"{len(texts['moves'])} golpes, {len(texts['abilities'])} habilidades -> {os.path.normpath(out2)} ({os.path.getsize(out2) // 1024} KB)")

# TMs e tutores (onde aprender cada golpe, por grupo de versoes) e a tabela TM/TR/HM -> golpe.
# text-info.json ganha:
#   "tm"/"tutor": {geracao: {chave do golpe: {grupo: texto}}}
#   "machines":   {geracao: {"TM01": {grupo ou "*": chave do golpe}}}
for kind in ('tm', 'tutor', 'machines'):
    texts[kind] = {}
for folder in folders:
    m = re.search(r'gen(\d+)$', folder)
    if not m:
        continue
    gen = m.group(1)
    path = os.path.join(folder, 'i18n', 'pt.json')
    if os.path.exists(path):
        pt = load(path)
        for kind, src in (('tm', 'tm_locations'), ('tutor', 'tutor_locations')):
            for slug, byGroup in (pt.get(src) or {}).items():
                if not isinstance(byGroup, dict):
                    continue
                clean = {g: t.strip() for g, t in byGroup.items() if isinstance(t, str) and t.strip() and not t.startswith('Not in')}
                if clean:
                    texts[kind].setdefault(gen, {})[key(slug)] = clean
    path = os.path.join(folder, 'machines.json')
    if os.path.exists(path):
        for mach in load(path):
            if not isinstance(mach, dict) or not mach.get('id') or not mach.get('move'):
                continue
            for group in mach.get('grupos') or ['*']:
                texts['machines'].setdefault(gen, {}).setdefault(mach['id'].upper(), {})[group] = key(mach['move'])

with open(out2, 'w', encoding='utf-8') as f:
    json.dump(texts, f, ensure_ascii=False, separators=(',', ':'))
print(f"TMs/tutores: {sum(len(v) for v in texts['tm'].values())} golpes com TM, {sum(len(v) for v in texts['tutor'].values())} com tutor, "
      f"{sum(len(v) for v in texts['machines'].values())} máquinas -> {os.path.normpath(out2)} ({os.path.getsize(out2) // 1024} KB)")
