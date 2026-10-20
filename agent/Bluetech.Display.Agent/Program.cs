using Bluetech.Display.Agent;

// BluetechDisplayAgent [--porta 5080] [--dati C:\percorso\dati]
// BluetechDisplayAgent --replay <cartella di una registrazione> [--velocita 2] [--ripeti true]
// Casse, stampanti, dispositivi, rete e password: dal back office (http://localhost:5080/backoffice), in dati\impostazioni.json.
do
{
    AgentApp.RestartRequested = false;
    await using var app = AgentApp.Build(args);
    await app.RunAsync();
}
while (AgentApp.RestartRequested);   // "Riavvia" dal back office, da console; da servizio lo riavvia Windows
