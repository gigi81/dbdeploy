Write-Output "Make sure sql server is running"
Write-Output "You can start a sql server instance using docker by running"
Write-Output "docker run --env=ACCEPT_EULA=Y --env=SA_PASSWORD=SqlServer2019! --network=bridge -p 1433:1433 --restart=always -d mcr.microsoft.com/mssql/server:2019-latest"

Write-Output "Now running dbdeploy..."
dbdeploy deploy --path $PSScriptRoot -c