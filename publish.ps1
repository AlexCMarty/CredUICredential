$apiKey = Get-Content -Path .\.apikey -Raw
$module = '.\CredUICredential.psd1'

Publish-Module -Path $module -NuGetApiKey $apiKey