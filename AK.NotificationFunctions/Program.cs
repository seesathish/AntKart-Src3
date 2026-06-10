using Microsoft.Extensions.Hosting;

// .NET 9 isolated-worker host for the AntKart notification Functions app.
//
// This process is deployed to the provisioned Function App ("func-antkart-notifications-dev").
// The Function App runs with a system-assigned MANAGED IDENTITY: any Azure resource this app
// later needs (Key Vault, an email service, ...) is reached through that identity — there are
// no secrets or connection strings in configuration.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
