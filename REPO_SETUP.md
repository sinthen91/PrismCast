# First repository setup

Target repository:

`https://github.com/Sinnsational/PrismCast`

## One-time setup

1. Create an empty public GitHub repository named `PrismCast` under the `Sinnsational` account.
2. Do not initialize it with a README, license, or .gitignore. This source tree already contains those files.
3. Upload/push the contents of this source tree. The full AGPL license and third-party notices are already included.
4. Run `build.cmd` on the Windows FFXIV machine and verify `dist\PrismCast-Dev\PrismCast.dll` loads as a Dalamud dev plugin.
5. After the first successful in-game host/viewer test, create tag/release `v0.2.0-alpha.1` and attach `dist\PrismCast.zip` as `PrismCast.zip`.
6. Friends can then add this custom repository URL to Dalamud:

   `https://raw.githubusercontent.com/Sinnsational/PrismCast/main/pluginmaster.json`

Do not publish the alpha release before a two-client test succeeds. Release automation can be enabled after the first known-good package so CI does not industrialize a broken build with impressive efficiency.
