dotnet restore
$env:ASPNETCORE_URLS="http://localhost:5173"
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project src/WebApp
