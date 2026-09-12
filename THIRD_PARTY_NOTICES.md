# Third-party notices

PrismCast is distributed under GNU AGPL-3.0-or-later. The complete license text is in `LICENSE.md`.

## FFXIV-Aetherphone / AetherStream

Parts of PrismCast's world-space video rendering and media-player design were developed from or informed by the open-source FFXIV-Aetherphone project.

Upstream project: https://github.com/XeldarAlz/FFXIV-Aetherphone

Reference commit used during the original proof of concept:
`f9682b2d350c212f275a42f88c7e5d798b761fc5`

Upstream notice:

> FFXIV-Aetherphone  
> Copyright (c) 2026 Xeldar Alz  
> This project is licensed under the GNU Affero General Public License v3.0.  
> Redistributions and derivative works must preserve existing copyright notices and license information as required by the AGPL.

PrismCast has its own name and identity and does not connect to the official Aethernet services.

## SharpCompress

PrismCast uses SharpCompress 0.50.4, licensed under the MIT License.

Copyright (c) 2014 Adam Hathcock.

The full notice is preserved in `licenses/SharpCompress-MIT.txt`.

## SharpDX

PrismCast uses SharpDX Direct3D11/D3DCompiler 4.2.0, licensed under the MIT License.

Copyright (c) 2010-2014 SharpDX - Alexandre Mutel.

The full notice is preserved in `licenses/SharpDX-MIT.txt`.

## Runtime components downloaded on demand

PrismCast downloads unmodified runtime components from their upstream release channels when needed:

- libmpv / mpv-winbuild
- yt-dlp
- Deno
- cloudflared
- FFmpeg

Those components retain their respective upstream licenses and are not re-licensed by PrismCast. PrismCast does not bundle those runtime binaries in its source repository.
