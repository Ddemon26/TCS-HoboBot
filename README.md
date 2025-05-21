# TCS HoboBot

*A Discord economy & casino bot built with **.NET 8** and **Discord.Net 3**.*

---

## Table of Contents

1. [Features](#features)
2. [Quick Start](#quick-start)
3. [Configuration](#configuration)
4. [Slash Commands](#slash-commands)
5. [Project Structure](#project-structure)
6. [Persistence](#persistence)
7. [Contributing](#contributing)
8. [License](#license)

---

## Features

* **Economy core** – persistent wallet with balance, cooldown‑gated jobs, begging, robbery, PvP fights and more.
* **Property system** – buy properties that yield passive income (`/property_buy`, `/property_collect`).
* **Casino suite** – full implementations of

    * Blackjack (interactive)
    * Baccarat (incl. side bets)
    * Craps
    * Roulette
    * Slots
* **Leaderboards** – `/top` shows the richest players on the server.
* **Moderation helpers** – `/clear` purges the bot’s own messages and dismisses open interactions.
* **Utility** – latency check (`/ping`), random dice/number rolls, roulette wheel spin.
* **Extensible architecture** – each feature lives in its own *Interaction Module*; add new commands by dropping a new `*.cs` file inside `Modules/`.

---

## Quick Start

> Requires **.NET 8 SDK** or later.

```bash
# 1. Clone & restore packages
git clone <repo‑url> && cd TCS.HoboBot
dotnet restore

# 2. Add your Discord bot token using the built‑in Secret Store
# (avoids committing the token to git)
dotnet user-secrets init
dotnet user-secrets set "DISCORD_TOKEN" "<your‑bot‑token>"

# 3. (Optional) point the bot at your guild during development
dotnet user-secrets set "GUILD_ID" "<your‑test‑guild‑id>" # (optional)


# 4. Build & run
dotnet run --project TCS.HoboBot
```

After the first run, slash‑commands are registered instantly in the *test* guild you specified via `GUILD_ID`. For production you can uncomment the global registration line (`RegisterCommandsGloballyAsync`) once you are ready to go live (global sync can take up to **1 hour**).

---

## Configuration

| Key             | How to set                           | Purpose                                                                                                       |
| --------------- | ------------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `DISCORD_TOKEN` | Secret Store or environment variable | **Required.** Bot login token obtained from [https://discord.com/developers](https://discord.com/developers). |
| `GUILD_ID`      | `BotService.cs` constant             | *Optional.* Guild to which commands are registered instantly while coding.                                    |

All other settings use sane defaults. If you need additional config points, inject `IConfiguration` into a module or service – `Program.cs` already wires `Host.CreateDefaultBuilder()` which loads *appsettings.json*, environment variables and user‑secrets.

---

## Slash Commands

Below is the command surface automatically extracted from the source – descriptions are the actual values passed to `[SlashCommand]` attributes.

| Command            | Module              | Description                                                                                    |
| ------------------ | ------------------- | ---------------------------------------------------------------------------------------------- |
| `beg`              | PingModule          | Hobo‑style begging on the streets!                                                             |
| `work`             | PingModule          | Work hard for your money!                                                                      |
| `offer`            | PingModule          | Offer these cheeseburgers!                                                                     |
| `prostitute`       | PingModule          | Prostitute yourself for money!                                                                 |
| `rob`              | PingModule          | Attempt to rob a user (max 30 % of their total, rare chance).                                  |
| `fight`            | PingModule          | Fight another user for money!                                                                  |
| `balance`          | PingModule          | Check your balance.                                                                            |
| `top`              | PingModule          | Check the top hobos!                                                                           |
| `spin`             | PingModule          | Spins the wheel!                                                                               |
| `roll`             | PingModule          | Rolls a random number from 1 to the provided max value.                                        |
| `diceplayer`       | PingModule          | Rolls a random number from 1 to the provided max value and compares it to another user’s roll. |
| `property_buy`     | BuyPropertyModule   | Buy a property                                                                                 |
| `property_collect` | BuyPropertyModule   | Collect money from your properties                                                             |
| `blackjack`        | BlackJackGameModule | Play an interactive blackjack hand vs the house.                                               |
| `baccarat`         | BaccaratModule      | Play Baccarat with optional side‑bets.                                                         |
| `craps`            | CrapsModule         | Play a game of Craps.                                                                          |
| `roulette`         | RouletteModule      | Play a game of Roulette.                                                                       |
| `slots`            | SlotMachineModule   | Pull a three‑reel slot machine.                                                                |
| `clear`            | ClearModule         | Clear all bot messages and dismiss open interactions.                                          |

> Tip: **Discord.Net 3.17.4** supports autocomplete, choices and modals – you can enrich existing commands by decorating parameters with the corresponding attributes.

---

## Persistence

Player wallets and property ownership are stored in **JSON** files (`wallets.json`, `properties.json`) in the working directory. The dictionaries are loaded on startup (`PlayersWallet.LoadAsync`) and saved again on shutdown.

* Data access is *thread‑safe* via `ConcurrentDictionary`.
* You can safely wipe the JSON to reset the economy while testing.

For distributed or large‑scale hosting consider swapping the JSON files for a proper database (Redis, SQL, etc.). The persistence layer is isolated inside `TCS.HoboBot.Data` – just replace the `LoadAsync` / `SaveAsync` implementations.

---

## Contributing

1. Fork and clone the repo.
2. Create a feature branch (`git checkout -b feat/my-awesome-idea`).
3. Commit your changes with clear messages.
4. Ensure `dotnet test` (if you add tests) passes and the bot still starts.
5. Open a Pull Request – please describe *why* the change is valuable.

Coding guidelines

* Follow **C# 12** conventions.
* Enable *Nullable Reference Types* where possible.
* New commands should live in their own file under `Modules/` and be decorated with `[SlashCommand]`.

---

## License

No explicit license file is present. Until one is added, treat the code as **All Rights Reserved** – ask the author before redistributing. 

*Last updated: 21 May 2025*
