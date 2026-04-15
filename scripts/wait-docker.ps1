$maxWait = 90
for ($i = 0; $i -lt $maxWait; $i += 5) {
    Start-Sleep -Seconds 5
    docker ps 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host "Docker ready after $i seconds"; exit 0 }
    Write-Host "Waiting... ${i}s"
}
Write-Host "Docker did not become ready"
exit 1
