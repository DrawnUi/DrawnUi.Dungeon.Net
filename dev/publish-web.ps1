# Publishes a web head of the game to its Cloudflare Pages site.
#   pwsh dev/publish-web.ps1 -Head Web      -> https://dungeonnet.drawnui.net    (pure WebAssembly, DrawnUi.Web)
#   pwsh dev/publish-web.ps1 -Head Blazor   -> https://dungeonblazor.drawnui.net (Blazor WebAssembly)
# Needs CLOUDFLARE_API_TOKEN (Pages write) in the environment and the DrawnUi source next to this folder.
# -Out reuses an existing publish folder (skips dotnet publish).
param(
    [ValidateSet('Web', 'Blazor')] [string] $Head = 'Web',
    [string] $Out = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = @{ Web = 'src/Dungeon.Web/Dungeon.Web.csproj'; Blazor = 'src/Dungeon.Blazor/Dungeon.Blazor.csproj' }[$Head]
$pages = @{ Web = 'dungeonnet-drawnui'; Blazor = 'dungeonblazor-drawnui' }[$Head]

if (-not $Out) {
    $Out = Join-Path ([IO.Path]::GetTempPath()) "dungeon-pub/$Head"
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    # No AOT for the site: an AOT dotnet.native.wasm (46 MB) is over Cloudflare Pages' 25 MiB per-file limit.
    # The interpreted Release build holds 60 FPS in the game (the Web head is interpreted too).
    dotnet publish (Join-Path $root $project) -c Release -o $Out -nologo -p:RunAOTCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}
$site = Join-Path $Out 'wwwroot'

# The drawnui.net zone lets browsers keep .js for hours: every script the page loads by a fixed name gets a
# ?v=<content hash> stamp, so a republish is seen at once (fingerprinted files keep their own names).
function Hash([string] $file) { (Get-FileHash $file -Algorithm SHA256).Hash.Substring(0, 12).ToLower() }
function Stamp([string] $file, [string] $pattern, [string] $target) {
    $text = [IO.File]::ReadAllText($file)
    $stamped = $text -replace [regex]::Escape($pattern), "$pattern`?v=$(Hash (Join-Path $site $target))"
    [IO.File]::WriteAllText($file, $stamped)
    Remove-Item "$file.br", "$file.gz" -ErrorAction SilentlyContinue   # stale precompressed copies
}
if ($Head -eq 'Web') {
    $main = Join-Path $site 'main.js'
    Stamp $main './_framework/dotnet.js' '_framework/dotnet.js'
    Stamp $main './_content/DrawnUi.Web/drawnui-web.js' '_content/DrawnUi.Web/drawnui-web.js'
    Stamp (Join-Path $site 'index.html') './main.js' 'main.js'
}

# The page and the unhashed entry scripts are revalidated on every load.
@"
/
  Cache-Control: no-cache
/index.html
  Cache-Control: no-cache
/main.js
  Cache-Control: no-cache
/_framework/dotnet.js
  Cache-Control: no-cache
/_framework/blazor.boot.json
  Cache-Control: no-cache
"@ | Set-Content -Encoding utf8 (Join-Path $site '_headers')

npx --yes wrangler@4 pages deploy $site --project-name $pages --branch master --commit-dirty=true
if ($LASTEXITCODE -ne 0) { throw "deploy failed" }
