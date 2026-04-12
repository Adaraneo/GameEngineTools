# Character File Logging

Character file logging keeps a human-readable global log and can mirror scoped character events into per-person/per-subsystem files:

- Global text: `logs/Characters/Characters.log`
- Global JSONL: `logs/Characters/Characters.jsonl`
- Scoped text: `logs/Characters/Person/{personId}/{Subsystem}.log`
- Scoped JSONL: `logs/Characters/Person/{personId}/{Subsystem}.jsonl`

Scoped paths are created only for events logged inside `BeginCharacterScope(...)` and only when `MirrorMode` is `GlobalAndScoped`. Subsystem file names are sanitized before they are used as file names.

## Text Format

Each text line starts with explicit metadata tokens:

```text
{realTimestamp} [W:{worldTime}] [Seq:{eventInstanceId}] [{level}] [P:{personId}] [S:{subsystem}] [Corr:{correlationId}] [Rel:{relatedPersonId}] [Tick:{tickKey}] {category} ({eventId}) :: {message}
```

Required tokens are real timestamp, `W`, `Seq`, and level. Optional tokens are emitted only when the scoped metadata exists. Exception details are appended after the message.

## JSONL Format

When `WriteJsonLines` is enabled, each text target has a compact JSON Lines companion. Each line serializes the normalized `CharactersLogEntry` model, including message, category, level, event id, exception fields, scope metadata, and `EventInstanceId`.

`EventInstanceId` is generated once per logical log event. If the event is mirrored to both global and scoped outputs, all mirrored writes reuse the same ID.

## CharacterLogScope

`CharacterLogScope` carries diagnostic metadata:

- `PersonId` and `Subsystem` are required.
- `CorrelationId`, `InteractionId`, `DecisionId`, `RelatedPersonId`, `LocationId`, and `TickKey` are optional.

Prefer `logger.BeginCharacterScope(...)` instead of constructing the scope directly at call sites.

## Options

`CharactersFileLoggerOptions` controls output:

- `MirrorMode`: `GlobalOnly` or `GlobalAndScoped`.
- `WriteTextLogs`: enables `.log` output.
- `WriteJsonLines`: enables `.jsonl` output.
- `WorldTimeTextAccessor`: supplies world time text without hardwiring the logger to a specific clock source.
- `LogsDirectoryPath`, `MinLevel`, and `UseUtcTimestamps` keep their existing roles.

Flush is explicit through `ICharactersLogControl.FlushAll()` and also happens during provider disposal.
