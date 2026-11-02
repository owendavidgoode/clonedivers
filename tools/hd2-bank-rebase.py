#!/usr/bin/env python3
"""Rebase authored Wwise media onto current native routing."""
import struct


def rebase_bank(audio, old: bytes, current: bytes) -> tuple[bytes, dict, dict]:
    """Keep current routing; copy only actual retained authored embedded media."""
    old_chunks, current_chunks = audio.chunks(old), audio.chunks(current)
    old_media, current_media = audio.media(old_chunks), audio.media(current_chunks)
    old_sounds = audio.sounds(old_chunks.get(b'HIRC', bytes(4)))
    current_sounds = audio.sounds(current_chunks.get(b'HIRC', bytes(4)))
    migrations, absent, codec_changes, plugin_overrides = {}, [], [], {}
    for identity in old_sounds.keys() & current_sounds.keys():
        old_codec, old_stream = struct.unpack_from('<IB', old_sounds[identity][1])
        codec, stream = struct.unpack_from('<IB', current_sounds[identity][1])
        if codec != old_codec:
            if identity in old_media:
                wem = old_media[identity]
                pos, fmt = 12, None
                if wem[:4] != b'RIFF' or wem[8:12] != b'WAVE':
                    raise ValueError('Authored media is not RIFF WAVE')
                while pos+8 <= len(wem):
                    tag, size = struct.unpack_from('<4sI', wem, pos)
                    if tag == b'fmt ':
                        fmt = struct.unpack_from('<H',wem,pos+8)[0]
                        break
                    pos += 8+size+(size%2)
                if fmt != {0x40001: 0xffff, 0x10001: 1}.get(codec):
                    fmt_data = wem[pos+8:pos+8+size]
                    wwise_pcm = fmt == 0xfffe and size == 24 and struct.unpack_from('<H',fmt_data,16)[0] == 6 and struct.unpack_from('<H',fmt_data,14)[0] == 16 and struct.unpack_from('<H',fmt_data,12)[0] == 2*struct.unpack_from('<H',fmt_data,2)[0]
                    if codec == 0x40001 and old_codec == 0x10001 and wwise_pcm:
                        plugin_overrides[identity] = old_codec
                    else:
                        raise ValueError(f'Embedded authored codec requires transcoding: {identity}/{old_codec:x}->{codec:x}/WEM={fmt}/extension={wem[pos+8+24:pos+8+40].hex()}')
            codec_changes.append({'media': identity, 'oldPlugin': old_codec, 'nativePlugin': codec, 'streamType': stream, 'needsEffectiveWemFormatCheck': identity not in old_media, 'authoredEmbeddedFormatVerified': identity in old_media})
        if old_stream != stream:
            if old_stream != 0 or stream not in (1, 2) or codec != 0x40001:
                raise ValueError(f'Unreviewed source migration {identity}: {old_stream}->{stream}, codec {codec:08x}, authored={identity in old_media}')
            if identity in old_media:
                migrations[identity] = old_media[identity]
            else:
                absent.append(identity)
    merged = {}
    for identity, payload in current_media.items():
        authored = old_media.get(identity)
        if authored is None:
            merged[identity] = payload
            continue
        source = current_sounds.get(identity)
        if source and source[1][4] == 1:
            prefetch = struct.unpack_from('<I', source[1], 13)[0]
            if prefetch != len(payload) or len(authored) < prefetch:
                raise ValueError('Native prefetch/media length contract needs review')
            merged[identity] = authored[:prefetch]
        else:
            merged[identity] = authored
    updated = current_chunks.copy()
    plugin_edits = []
    if plugin_overrides:
        hirc = bytearray(updated[b'HIRC'])
        at = 4
        for _ in range(struct.unpack_from('<I', hirc)[0]):
            kind, size, sound_id = struct.unpack_from('<BII', hirc, at)
            if kind == 2:
                media_id = struct.unpack_from('<I', hirc, at+14)[0]
                if media_id in plugin_overrides:
                    before = bytes(hirc[at+9:at+13])
                    struct.pack_into('<I',hirc,at+9,plugin_overrides[media_id])
                    plugin_edits.append({'offset': at+9, 'sound': sound_id, 'media': media_id, 'beforeHex': before.hex(), 'afterHex': bytes(hirc[at+9:at+13]).hex()})
            at += 5+size
        updated[b'HIRC'] = bytes(hirc)
    if merged:
        table, data = bytearray(), bytearray()
        for identity, payload in merged.items():
            data.extend(bytes(-len(data) % 16))
            table.extend(struct.pack('<III', identity, len(data), len(payload)))
            data.extend(payload)
        updated[b'DIDX'], updated[b'DATA'] = bytes(table), bytes(data)
    body = b''.join(struct.pack('<4sI', tag, len(payload)) + payload for tag, payload in updated.items())
    wrapper = bytearray(current[:16])
    struct.pack_into('<I', wrapper, 4, len(body))
    result = bytes(wrapper) + body
    check = audio.chunks(result)
    reversed_hirc = bytearray(check.get(b'HIRC', b''))
    for edit in plugin_edits:
        reversed_hirc[edit['offset']:edit['offset']+4] = bytes.fromhex(edit['beforeHex'])
    if bytes(reversed_hirc) != current_chunks.get(b'HIRC', b'') or audio.media(check) != merged or any(check[tag] != value for tag, value in current_chunks.items() if tag not in (b'DIDX', b'DATA', b'HIRC')):
        raise ValueError('Current routing/merged media invariant')
    return result, migrations, {'currentNativeSha256': audio.V.sha(current), 'currentHierarchyPreservedExceptDeclaredSourceCodec': True, 'sourceCodecEdits': plugin_edits, 'authoredEmbeddedSamplesRetained': len(old_media.keys() & merged.keys()), 'newNativeEmbeddedSamplesPreserved': len(merged.keys()-old_media.keys()), 'migratedNativeOnlyAbsentFromOldMedia': absent, 'codecChanges': codec_changes, 'obsoleteOldMedia': sorted(old_media.keys()-merged.keys()-migrations.keys())}
