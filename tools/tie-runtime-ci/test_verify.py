"""Offline contract tests: no hosted operations, build tools or game access."""
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zipfile

import verify as v


def pe_fixture() -> tuple[bytes,list[tuple[int,str]]]:
    data=bytearray(2048)
    struct.pack_into('<I',data,0x3C,0x80)
    data[0x80:0x84]=b'PE\0\0'
    struct.pack_into('<H',data,0x86,1)
    struct.pack_into('<H',data,0x94,0xF0)
    struct.pack_into('<H',data,0x98,0x20B)
    struct.pack_into('<Q',data,0x98+24,0x180000000)
    struct.pack_into('<III',data,0x98+0xF0+12,0x1000,0x600,0x200)
    rows=[(0x1111222233334444,'first'),(0x5555666677778888,'second'),(v.BASE,'packages/generated/loadout/eagle_base')]
    for index,(key,name) in enumerate(rows):
        raw=0x400+64*index
        struct.pack_into('<QQ',data,0x240+16*index,key,0x180000000+0x1000+raw-0x200)
        text=name.encode()+b'\0'
        data[raw:raw+len(text)]=text
    return bytes(data),rows


class Tests(unittest.TestCase):
    def test_actual_pe_rows_and_mutations(self):
        data,rows=pe_fixture()
        self.assertEqual(v.compiled_catalogue(data,rows),0x240)
        for offset in (0x80,0x98,0x240+32,0x400,0x240+8):
            mutated=bytearray(data);mutated[offset]^=0x80
            with self.subTest(offset=offset),self.assertRaises((ValueError,struct.error,UnicodeDecodeError)):
                v.compiled_catalogue(bytes(mutated),rows)

    def test_zip_alias_escape_and_duplicate_reject(self):
        for names in (('ok','OK'),('../escape',),('/absolute',),('a/../b',),('C:/input',),('a\\b','a/b')):
            stream=io.BytesIO()
            with zipfile.ZipFile(stream,'w') as archive:
                for name in names:archive.writestr(name,b'test')
            with zipfile.ZipFile(stream) as archive,self.assertRaises(ValueError):v.entries(archive)
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,'w') as archive:archive.writestr('app\\one.dll',b'legitimate upstream Windows separator')
        with zipfile.ZipFile(stream) as archive:self.assertEqual(list(v.entries(archive)),['app/one.dll'])

    def test_json_duplicate_reject(self):
        with self.assertRaises(ValueError):v.json_bytes(b'{"files":{},"files":{}}')

    def test_catalogue_duplicate_reject(self):
        source=b'static const RefPackage kRefPackages[] = {\n{0x1ull, "one"},\n{0x1ull, "two"},\n};'
        with self.assertRaises(ValueError):v.catalogue(source)

    def test_pair_same_path_prefix_and_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            out=Path(temporary)
            baseline={'sourcePath':'D:/a','catalogue':[[1,'one']], 'engineFiles':{'one':'abc'},'payloadFiles':{'app/one':'abc'}}
            patched={**baseline,'catalogue':[[1,'one'],[v.BASE,'eagle']], 'engineFiles':{'one':'def'}}
            def write():
                (out/'baseline-verification.json').write_text(json.dumps(baseline))
                (out/'patched-verification.json').write_text(json.dumps(patched))
            write();v.compare(out)
            result=json.loads((out/'pair-verification.json').read_text())
            self.assertFalse(result['releaseAccepted'])
            for key,value in (('sourcePath','D:/other'),('catalogue',[[2,'changed'],[v.BASE,'eagle']]),('engineFiles',{'extra':'abc'})):
                saved=patched[key];patched[key]=value;write()
                with self.subTest(key=key),self.assertRaises(ValueError):v.compare(out)
                patched[key]=saved


if __name__=='__main__':unittest.main()
