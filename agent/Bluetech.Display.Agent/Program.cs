using Bluetech.Display.Agent;

// BluetechDisplayAgent [--file C:\percorso\Scontrino.txt] [--porta 5080]
// BluetechDisplayAgent --replay <cartella di una registrazione> [--velocita 2] [--ripeti true]
await AgentApp.Build(args).RunAsync();
