using Bluetech.Display.Agent;
using Bluetech.Display.Agent.Accesso;

// BluetechDisplayAgent [--porta 5080] [--dati C:\percorso\dati]
// BluetechDisplayAgent --replay <cartella di una registrazione> [--velocita 2] [--ripeti true]
// BluetechDisplayAgent --genera-password [--dati …]: una password nuova per il back office, che vale dal prossimo avvio
// Casse, stampanti, dispositivi, rete e password: dal back office (http://localhost:5080/backoffice), in dati\impostazioni.json.
if (args.Contains("--genera-password"))
    return PasswordFile.GenerateCommand(AgentApp.DataFolder(args.Where(a => a != "--genera-password").ToArray()), Console.Out, Console.Error);

do
{
    AgentApp.RestartRequested = false;
    await using var app = AgentApp.Build(args);
    await app.RunAsync();
}
while (AgentApp.RestartRequested);   // "Riavvia" dal back office, da console; da servizio lo riavvia Windows
return 0;
