# TCS HoboBot

*A feature‑rich Discord **economy**, **casino** **& music** bot built with **.NET 8** and **Discord.Net 3**.*

---

## Table of Contents

1. [Features](#features)
2. [Quick Start](#quick-start)
3. [Configuration](#configuration)
4. [Slash Commands](#slash-commands)
5. [Project Structure](#project-structure)
6. [Persistence](#persistence)
7. [Contributing](#contributing)
8. [License](#license)

---

## Features

* **Economy core** – persistent wallet with balances, cooldown‑gated jobs (`/work`), begging (`/beg`), prostitution, robbery, PvP fights and more.
* **Property system** – purchase properties that yield passive income (`/property_buy`, `/property_collect`).
* **Casino suite** – full implementations of

  * Blackjack (interactive)
  * Baccarat (incl. side bets)
  * Craps
  * Roulette
  * Slots (classic 3‑reel, 3 × 3 grid & 5 × 5 advanced)
  * Random chance wheel (`/spin`)
* **Music playback** – stream audio from YouTube in voice channels with queue, volume, skipping & now‑playing (`/play`, `/music_queue`, `/music_volume`, …).
* **Leaderboards** – `/top` shows the richest players on the server.
* **Moderation helpers** – `/clear` purges the bot’s own messages and dismisses open interactions, `/giverole` & `/takerole`.
* **Utility** – latency check (`/ping`), random dice/number rolls (`/roll`), jackpots check, etc.
* **Extensible architecture** – each feature lives in its own *Interaction Module* (`IInteractionModule`). Add new commands by dropping a new `*.cs` file inside `Modules/`.

---

## Quick Start

> Requires **.NET 8 SDK** or later **plus** FFmpeg and yt‑dlp in your *PATH* for music playback.

```bash
# 1. Clone & restore packages
git clone <repo-url> && cd TCS.HoboBot
dotnet restore

# 2. Add your Discord bot token using the built‑in Secret Store
#    (avoids committing the token to git)
dotnet user-secrets set "DISCORD_TOKEN" "<your-bot-token>"

# 3. (Optional) point the bot at your guild during development
dotnet user-secrets set "GUILD_ID" "<your-test-guild-id>"

# 4. Build & run
dotnet run --project TCS.HoboBot
```

After the first run, slash‑commands are registered instantly in the guild specified by `GUILD_ID`.
Remove that setting when you are ready to go live (global sync can take up to **1 hour**).

---

## Configuration

| Key             | How to set                           | Purpose                                                                                        |
| --------------- | ------------------------------------ | ---------------------------------------------------------------------------------------------- |
| `DISCORD_TOKEN` | Secret Store or environment variable | **Required.** Bot token from [https://discord.com/developers](https://discord.com/developers). |
| `GUILD_ID`      | Secret Store or environment variable | During development, limits slash‑command sync to your test guild for instant updates.          |

All other settings use sane defaults. The bot automatically loads *appsettings.json*, environment variables and user‑secrets.

---

## Slash Commands

Below is the command surface automatically extracted from the source as of **26 May 2025**.

| Command            | Description                                                                                          |
| ------------------ | ---------------------------------------------------------------------------------------------------- |
| `add_shmeckles`    | give yourself some shmeckles                                                                         |
| `baccarat`         | Play Baccarat with optional side‑bets.                                                               |
| `balance`          | Check your balance.                                                                                  |
| `beg`              | Hobo-style begging on the streets!                                                                   |
| `blackjack`        | Play an interactive blackjack hand vs the house.                                                     |
| `check_shmeckles`  | give yourself some shmeckles                                                                         |
| `check_stash`      | Check your stash                                                                                     |
| `clear`            | Clear all bot messages and dismiss open interactions.                                                |
| `cook`             | Cook some drugs                                                                                      |
| `craps`            | Play a game of Craps.                                                                                |
| `diceplayer`       | Rolls a random number from 1 to 100 and compares it to another user's roll.                          |
| `fight`            | Fight another user for money!                                                                        |
| `flowerpoker`      | Plants five flowers for each player and decides the winner using classic Flower‑Poker hand rankings. |
| `giverole`         | Give a role to a member                                                                              |
| `grow`             | Grow some weed or shrooms                                                                            |
| `hayko`            | some gay shit                                                                                        |
| `hayko_echo`       | some gay shit                                                                                        |
| `jackpots_check`   | Check the current jackpots for this guild                                                            |
| `leave`            | Disconnect the bot from the voice channel.                                                           |
| `music_nowplaying` | Shows details about the currently playing track.                                                     |
| `music_play`       | Play a song from YouTube by URL or search query.                                                     |
| `music_queue`      | Shows the current music queue.                                                                       |
| `music_skip`       | Skips the current track.                                                                             |
| `music_stop`       | Stops playback, clears the queue, and leaves the voice channel.                                      |
| `music_volume`     | Sets the playback volume (1-150%).                                                                   |
| `ping`             | Replies with pong and gateway latency.                                                               |
| `play`             | Play a YouTube video in your voice channel.                                                          |
| `property_buy`     | Buy a property                                                                                       |
| `property_check`   | Check your properties                                                                                |
| `property_collect` | Collect money from your properties                                                                   |
| `prostitute`       | Prostitute yourself for money!                                                                       |
| `remove_shmeckles` | give yourself some shmeckles                                                                         |
| `rob`              | Attempt to rob a user (max 30% of their total, rare chance).                                         |
| `roll`             | Rolls a random number from 1 to the provided max value.                                              |
| `roulette`         | Play a game of Roulette.                                                                             |
| `sell_stash`       | Sell your stash                                                                                      |
| `skip`             | Skip the current track.                                                                              |
| `slots`            | Pull a three‑reel slot machine.                                                                      |
| `slots3x3`         | Play a 3x3 grid slot machine.                                                                        |
| `slots5x5`         | Play a 5-reel, 5-row advanced slot machine.                                                          |
| `slots_classic`    | Pull a three-reel slot machine.                                                                      |
| `spend_shmeckles`  | give yourself some shmeckles                                                                         |
| `spin`             | Spins the wheel!                                                                                     |
| `takerole`         | Remove a role from a member                                                                          |
| `top`              | Check the top hobos!                                                                                 |
| `work`             | Work hard for your money!                                                                            |

---

## Project Structure

```
TCS.HoboBot/
 ├─ ActionEvents/          # One‑off interaction helper classes
 ├─ Data/                  # Persistence & domain services (cooldowns, economy, etc.)
 ├─ Modules/               # Economy, casino, PvP, moderation & utility slash‑command modules
 ├─ YoutubeMusic/          # Music playback implementation (FFmpeg + yt‑dlp)
 ├─ BotService.cs          # Top‑level DI & startup wiring
 ├─ Program.cs             # Entry point
 └─ TCS.HoboBot.csproj     # SDK‑style project file
```

---

## Persistence

By default the bot serialises all data to a local `data/` folder.
During development you can safely wipe the JSON to reset the economy.

For distributed or large‑scale hosting you can swap the JSON store for a database – just replace the `LoadAsync` / `SaveAsync` implementations in `Data/*` classes.

---

## Contributing

1. Fork and clone the repo.
2. Create a feature branch (`git checkout -b feat/my-awesome-idea`).
3. Commit your changes with clear messages.
4. Ensure `dotnet test` passes and the bot still starts.
5. Open a Pull Request – please describe *why* the change is valuable.

**Coding guidelines**

* Follow **C# 12** conventions.
* Enable *Nullable Reference Types* where possible.
* New commands should live in their own file under `Modules/` and be decorated with `[SlashCommand]`.

---

## License

No explicit license file is present. Until one is added, treat the source as **All Rights Reserved** – ask the 
author before redistributing.

*Last updated: 26 May 2025*
