# Publishes a web head of the game to its Cloudflare Pages site.
#   pwsh dev/publish-web.ps1 -Head Web      -> https://dungeonnet.drawnui.net    (pure WebAssembly, DrawnUi.Web)
#   pwsh dev/publish-web.ps1 -Head Blazor   -> https://dungeonblazor.drawnui.net (Blazor WebAssembly)
# Needs CLOUDFLARE_API_TOKEN (Pages write) in the environment.
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
    # A clean Release state first: an earlier AOT publish (the Blazor head's Release default) leaves assemblies with
    # their IL stripped in obj, and an interpreted publish reuses them: the app dies at start in the interpreter
    # ("NIY encountered", "runtime and class libraries are out of sync").
    $projectDir = Split-Path (Join-Path $root $project) -Parent
    Remove-Item -Recurse -Force (Join-Path $projectDir 'obj/Release'), (Join-Path $projectDir 'bin/Release') -ErrorAction SilentlyContinue
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
    $index = Join-Path $site 'index.html'
    Stamp $index './main.js' 'main.js'
    # drawnui-web.js is imported twice, by main.js and by DrawnUi.Web's C# (JSHost.ImportAsync, by its plain path):
    # both must load ONE module, or the canvas's input and resize callbacks go to a second copy no one wired. An
    # import map stamps that one URL for both imports.
    $web = './_content/DrawnUi.Web/drawnui-web.js'
    $map = "<script type=`"importmap`">{ `"imports`": { `"$web`": `"$web`?v=$(Hash (Join-Path $site '_content/DrawnUi.Web/drawnui-web.js'))`" } }</script>"
    $text = [IO.File]::ReadAllText($index)
    [IO.File]::WriteAllText($index, ($text -replace '(\s*)(<script type="module")', "`$1$map`$1`$2"))
    Remove-Item "$index.br", "$index.gz" -ErrorAction SilentlyContinue
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
