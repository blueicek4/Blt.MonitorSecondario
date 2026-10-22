using Bluetech.Display.Agent;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Impostazioni;

// BluetechDisplayAgent [--porta 5080] [--dati C:\percorso\dati]
// BluetechDisplayAgent --replay <cartella di una registrazione> [--velocita 2] [--ripeti true]
// BluetechDisplayAgent --genera-password [tecnico] [--dati …]: una password nuova per il back office (del titolare, o del
// tecnico), che vale dal prossimo avvio
// Casse, stampanti, schermi, rete e password: dal back office (http://localhost:5080/backoffice), in dati\impostazioni.json.
if (args.Contains("--genera-password"))
{
    var role = args.Contains(AccessEntry.Technician) ? AccessEntry.Technician : AccessEntry.Owner;
    var rest = args.Where(a => a != "--genera-password" && a != AccessEntry.Technician).ToArray();
    return PasswordFile.GenerateCommand(AgentApp.DataFolder(rest), Console.Out, Console.Error, role);
}

do
{
    AgentApp.RestartRequested = false;
    await using var app = AgentApp.Build(args);
    await app.RunAsync();
}
while (AgentApp.RestartRequested);   // "Riavvia" dal back office, da console; da servizio lo riavvia Windows
return 0;
