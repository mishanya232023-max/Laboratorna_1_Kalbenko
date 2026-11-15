param(
    [Parameter(Mandatory = $true)][string]$User,
    [Parameter(Mandatory = $true)][string]$Incident,
    [string]$Title = 'Changed by Alice (IDOR)',
    [string]$BaseUrl = 'http://localhost:5080'
)
if (-not $env:SeedUsers__Password) { 'SeedUsers__Password not set'; return }
$known = @{ alice = '20000000-0000-0000-0000-000000000001'; bob = '20000000-0000-0000-0000-000000000002' }
$incidentId = if ($known.ContainsKey($Incident)) { $known[$Incident] } else { $Incident }
function Send([string]$Method, [string]$Path, $Session, $Body) {
    $req = @{ Uri = "$BaseUrl$Path"; Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($Session) { $req.WebSession = $Session }
    if ($null -ne $Body) { $req.Body = ($Body | ConvertTo-Json); $req.ContentType = 'application/json; charset=utf-8' }
    try {
        $r = Invoke-WebRequest @req
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        $code = 0
        if ($resp) { $code = [int]$resp.StatusCode }
        return [pscustomobject]@{ Status = $code; Body = '' }
    }
}
function Read-Title($Session) {
    $r = Send 'GET' "/api/incidents/$incidentId" $Session $null
    if ($r.Status -eq 200) { return ($r.Body | ConvertFrom-Json).title }
    return "http_$($r.Status)"
}
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Send 'POST' '/api/auth/login' $session @{ userName = $User; password = $env:SeedUsers__Password }
"login_status=$($login.Status)"
if ($login.Status -ge 300) { return }
"user=$User"
"title_before=$(Read-Title $session)"
$put = Send 'PUT' "/api/incidents/$incidentId" $session @{ title = $Title }
"put_status=$($put.Status)"
"title_after=$(Read-Title $session)"
