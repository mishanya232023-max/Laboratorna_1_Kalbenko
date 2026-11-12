param(
    [Parameter(Mandatory = $true)][string]$User,
    [Parameter(Mandatory = $true)][string]$Incident,
    [string]$BaseUrl = 'http://localhost:5080'
)

if (-not $env:SeedUsers__Password) { 'SeedUsers__Password not set'; return }

$known = @{
    alice   = '20000000-0000-0000-0000-000000000001'
    bob     = '20000000-0000-0000-0000-000000000002'
    unknown = [guid]::NewGuid().ToString()
}
$incidentId = if ($known.ContainsKey($Incident)) { $known[$Incident] } else { $Incident }

function Send([string]$Method, [string]$Path, $Session, $Body) {
    $req = @{ Uri = "$BaseUrl$Path"; Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($Session) { $req.WebSession = $Session }
    if ($null -ne $Body) { $req.Body = ($Body | ConvertTo-Json); $req.ContentType = 'application/json' }
    try {
        $r = Invoke-WebRequest @req
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        $text = ''
        $code = 0
        if ($resp) {
            $code = [int]$resp.StatusCode
            $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $text = $reader.ReadToEnd()
        }
        return [pscustomobject]@{ Status = $code; Body = $text }
    }
}

$session = $null
if ($User -ne 'anonymous') {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $login = Send 'POST' '/api/auth/login' $session @{ userName = $User; password = $env:SeedUsers__Password }
    "login_status=$($login.Status)"
    if ($login.Status -ge 300) { return }
    $me = Send 'GET' '/api/me' $session $null
    if ($me.Status -eq 200) {
        $m = $me.Body | ConvertFrom-Json
        "me: status=200 id=$($m.userId) user=$($m.userName) role=$($m.role)"
    } else { "me: status=$($me.Status)" }
}

$read = Send 'GET' "/api/incidents/$incidentId" $session $null
"read_status=$($read.Status)"
if ($read.Status -eq 200) {
    $j = $read.Body | ConvertFrom-Json
    "id=$($j.id) title=$($j.title) owner=$($j.ownerDisplayName)"
} else { $read.Body }
