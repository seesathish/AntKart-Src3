using AK.BuildingBlocks.Email;
using Microsoft.Extensions.Hosting;

// .NET 9 isolated-worker host for the AntKart notification Functions app.
//
// This process is deployed to the provisioned Function App ("func-antkart-notifications-dev").
// The Function App runs with a system-assigned MANAGED IDENTITY: any Azure resource this app
// needs is reached through that identity — there are no secrets or connection strings in
// configuration. Here that resource is Azure Communication Services (ACS) Email: AddAcsEmailSender
// builds the EmailClient from Acs:Endpoint + DefaultAzureCredential (no key).
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddAcsEmailSender(context.Configuration);
    })
    .Build();

host.Run();
