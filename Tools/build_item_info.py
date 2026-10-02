"""Gera PKHeX.Modern/Assets/item-info.json a partir dos dados do AllGenWiki (projeto HoennKantoWiki).

Uso: python Tools/build_item_info.py [caminho do HoennKantoWiki]

Junta, por item e por geracao:
- descricao em portugues (data/genN/item-descriptions.json);
- como obter itens-chave (data/genN/key-items.json);
- onde encontrar itens de evolucao por versao (data/genN/item-locations.json).

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
