"""Verifica os segmentos ELF das bibliotecas 64-bit de um APK para paginas de 16 KB."""
import argparse
from collections import Counter
import struct
import zipfile


def verify(path):
    checked = Counter()
    failures = []
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            parts = entry.filename.split('/')
            if len(parts) != 3 or parts[0] != 'lib' or parts[1] not in ('arm64-v8a', 'x86_64') or not parts[2].endswith('.so'):
                continue
            data = archive.read(entry)
            if data[:4] != b'\x7fELF' or data[4] != 2 or data[5] != 1:
                failures.append(f'{entry.filename}: formato ELF64 little-endian esperado')
                continue
            offset = struct.unpack_from('<Q', data, 32)[0]
            size, count = struct.unpack_from('<HH', data, 54)
            loads = 0
            for i in range(count):
                header = offset + i * size
                kind, _flags, file_offset, vaddr, _paddr, _filesz, _memsz, alignment = struct.unpack_from('<IIQQQQQQ', data, header)
                if kind != 1:
                    continue
                loads += 1
                if alignment < 16384 or (vaddr - file_offset) % 16384:
                    failures.append(f'{entry.filename}: PT_LOAD {i}, alinhamento {alignment}')
            if loads == 0:
                failures.append(f'{entry.filename}: sem segmentos PT_LOAD')
            checked[parts[1]] += 1
    if not checked:
        raise ValueError('APK sem bibliotecas 64-bit verificaveis')
    for abi, count in sorted(checked.items()):
        print(f'{abi}: {count} bibliotecas ELF verificadas')
    if failures:
        raise ValueError('\n'.join(failures))
    print('OK: segmentos PT_LOAD compativeis com paginas de 16 KB. Execucao em aparelho ainda precisa de teste.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk')
    verify(parser.parse_args().apk)
