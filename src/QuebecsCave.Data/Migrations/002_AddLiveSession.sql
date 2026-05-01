/*
    Adds dbo.LiveSession — captures what game/title was being broadcast and
    when, so the downloader can match a Twitch VOD (which carries no game
    info) to the game that was actually being played during that session.

    LastSeenAt: bumped every poll while the stream is up. Lets us treat the
    session as "still active until LastSeenAt + small grace window" even if
    we never observe an explicit transition to offline.

    EndedAt: set when we observe the stream go offline. Null while live.

    ResolvedGameId: when the LiveSessionTracker resolves the Twitch game ID
    to a row in dbo.Game, this points at it. Used directly by the downloader
    on ingest.
*/

CREATE TABLE [dbo].[LiveSession] (
    [LiveSessionId]      INT             IDENTITY(1,1) NOT NULL,
    [BroadcasterUserId]  VARCHAR(50)     NOT NULL,
    [TwitchGameId]       VARCHAR(50)     NULL,
    [GameName]           NVARCHAR(150)   NULL,
    [Title]              NVARCHAR(300)   NULL,
    [StartedAt]          DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt]         DATETIMEOFFSET(7) NOT NULL,
    [EndedAt]            DATETIMEOFFSET(7) NULL,
    [ResolvedGameId]     INT             NULL,
    CONSTRAINT [PK_LiveSession] PRIMARY KEY CLUSTERED ([LiveSessionId]),
    CONSTRAINT [FK_LiveSession_Game] FOREIGN KEY ([ResolvedGameId])
        REFERENCES [dbo].[Game]([GameId])
);

CREATE INDEX [IX_LiveSession_Broadcaster_Started]
    ON [dbo].[LiveSession] ([BroadcasterUserId], [StartedAt] DESC);

CREATE INDEX [IX_LiveSession_OpenSessions]
    ON [dbo].[LiveSession] ([BroadcasterUserId])
    WHERE [EndedAt] IS NULL;
