/*
    Quebec's Cave — Initial schema
    -----------------------------------------------------------------
    All datetime columns are datetimeoffset(7) and use the *At suffix.
    Tables are singular PascalCase. Junction/reference tables sit
    alongside their owning entities.

    DO NOT edit this script after first deploy. Add new migrations as
    002_*.sql, 003_*.sql, etc.
*/

------------------------------------------------------------------
-- Reference / lookup tables
------------------------------------------------------------------

CREATE TABLE [dbo].[Role] (
    [RoleId]            INT             IDENTITY(1,1) NOT NULL,
    [Name]              VARCHAR(20)     NOT NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY CLUSTERED ([RoleId]),
    CONSTRAINT [UQ_Role_Name] UNIQUE ([Name])
);

INSERT INTO [dbo].[Role] ([Name]) VALUES
    ('Streamer'),
    ('Moderator'),
    ('Developer'),
    ('Viewer');

CREATE TABLE [dbo].[ErrorStatus] (
    [ErrorStatusId]     INT             IDENTITY(1,1) NOT NULL,
    [Name]              VARCHAR(30)     NOT NULL,
    CONSTRAINT [PK_ErrorStatus] PRIMARY KEY CLUSTERED ([ErrorStatusId]),
    CONSTRAINT [UQ_ErrorStatus_Name] UNIQUE ([Name])
);

INSERT INTO [dbo].[ErrorStatus] ([Name]) VALUES
    ('Open'),
    ('Acknowledged'),
    ('RaisedToGit'),
    ('Fixed'),
    ('NonIssue');

------------------------------------------------------------------
-- Identity
------------------------------------------------------------------

CREATE TABLE [dbo].[User] (
    [UserId]            INT             IDENTITY(1,1) NOT NULL,
    [TwitchUserId]      VARCHAR(50)     NOT NULL,
    [TwitchLogin]       VARCHAR(50)     NOT NULL,
    [DisplayName]       NVARCHAR(100)   NOT NULL,
    [AvatarUrl]         VARCHAR(500)    NULL,
    [ThemePreference]   VARCHAR(10)     NULL,
    [TimeZoneId]        VARCHAR(100)    NULL,
    [CreatedAt]         DATETIMEOFFSET(7) NOT NULL,
    [LastSeenAt]        DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([UserId]),
    CONSTRAINT [UQ_User_TwitchUserId] UNIQUE ([TwitchUserId])
);

CREATE TABLE [dbo].[Developer] (
    [DeveloperId]       INT             IDENTITY(1,1) NOT NULL,
    [TwitchUserId]      VARCHAR(50)     NOT NULL,
    [TwitchLogin]       VARCHAR(50)     NOT NULL,
    [AddedByUserId]     INT             NOT NULL,
    [AddedAt]           DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Developer] PRIMARY KEY CLUSTERED ([DeveloperId]),
    CONSTRAINT [UQ_Developer_TwitchUserId] UNIQUE ([TwitchUserId]),
    CONSTRAINT [FK_Developer_AddedBy] FOREIGN KEY ([AddedByUserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE TABLE [dbo].[ModeratorCache] (
    [ModeratorCacheId]  INT             IDENTITY(1,1) NOT NULL,
    [TwitchUserId]      VARCHAR(50)     NOT NULL,
    [TwitchLogin]       VARCHAR(50)     NOT NULL,
    [RefreshedAt]       DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ModeratorCache] PRIMARY KEY CLUSTERED ([ModeratorCacheId]),
    CONSTRAINT [UQ_ModeratorCache_TwitchUserId] UNIQUE ([TwitchUserId])
);

CREATE TABLE [dbo].[TwitchToken] (
    [TwitchTokenId]     INT             IDENTITY(1,1) NOT NULL,
    [UserId]            INT             NOT NULL,
    [AccessToken]       VARBINARY(MAX)  NOT NULL,
    [RefreshToken]      VARBINARY(MAX)  NOT NULL,
    [ExpiresAt]         DATETIMEOFFSET(7) NOT NULL,
    [Scopes]            VARCHAR(500)    NOT NULL,
    CONSTRAINT [PK_TwitchToken] PRIMARY KEY CLUSTERED ([TwitchTokenId]),
    CONSTRAINT [FK_TwitchToken_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE TABLE [dbo].[LoginAttempt] (
    [LoginAttemptId]    INT             IDENTITY(1,1) NOT NULL,
    [TwitchUserId]      VARCHAR(50)     NULL,
    [IpHash]            VARBINARY(32)   NOT NULL,
    [SucceededAt]       DATETIMEOFFSET(7) NULL,
    [FailureReason]     VARCHAR(200)    NULL,
    [AttemptedAt]       DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_LoginAttempt] PRIMARY KEY CLUSTERED ([LoginAttemptId])
);

CREATE INDEX [IX_LoginAttempt_AttemptedAt] ON [dbo].[LoginAttempt] ([AttemptedAt] DESC);

------------------------------------------------------------------
-- Content
------------------------------------------------------------------

CREATE TABLE [dbo].[Game] (
    [GameId]            INT             IDENTITY(1,1) NOT NULL,
    [Name]              NVARCHAR(150)   NOT NULL,
    [Slug]              VARCHAR(150)    NOT NULL,
    [IconUrl]           VARCHAR(500)    NULL,
    [TwitchGameId]      VARCHAR(50)     NULL,
    [IsCustomIcon]      BIT             NOT NULL CONSTRAINT [DF_Game_IsCustomIcon] DEFAULT (0),
    [CreatedAt]         DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Game] PRIMARY KEY CLUSTERED ([GameId]),
    CONSTRAINT [UQ_Game_Slug] UNIQUE ([Slug])
);

CREATE TABLE [dbo].[Stream] (
    [StreamId]          INT             IDENTITY(1,1) NOT NULL,
    [Title]             NVARCHAR(300)   NOT NULL,
    [Description]       NVARCHAR(MAX)   NULL,
    [GameId]            INT             NOT NULL,
    [StreamedAt]        DATETIMEOFFSET(7) NOT NULL,
    [DurationSeconds]   INT             NOT NULL,
    [VideoUrl]          VARCHAR(500)    NOT NULL,
    [ThumbnailUrl]      VARCHAR(500)    NULL,
    [TwitchVodId]       VARCHAR(50)     NULL,
    [CreatedAt]         DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Stream] PRIMARY KEY CLUSTERED ([StreamId]),
    CONSTRAINT [FK_Stream_Game] FOREIGN KEY ([GameId]) REFERENCES [dbo].[Game]([GameId])
);

CREATE INDEX [IX_Stream_StreamedAt] ON [dbo].[Stream] ([StreamedAt] DESC);
CREATE INDEX [IX_Stream_GameId] ON [dbo].[Stream] ([GameId]);

CREATE TABLE [dbo].[Emoji] (
    [EmojiId]           INT             IDENTITY(1,1) NOT NULL,
    [Code]              VARCHAR(50)     NOT NULL,
    [Name]              VARCHAR(100)    NOT NULL,
    [ImageUrl]          VARCHAR(500)    NOT NULL,
    [IsActive]          BIT             NOT NULL CONSTRAINT [DF_Emoji_IsActive] DEFAULT (1),
    [SortOrder]         INT             NOT NULL CONSTRAINT [DF_Emoji_SortOrder] DEFAULT (0),
    CONSTRAINT [PK_Emoji] PRIMARY KEY CLUSTERED ([EmojiId]),
    CONSTRAINT [UQ_Emoji_Code] UNIQUE ([Code])
);

CREATE TABLE [dbo].[Reaction] (
    [ReactionId]        INT             IDENTITY(1,1) NOT NULL,
    [StreamId]          INT             NOT NULL,
    [UserId]            INT             NOT NULL,
    [EmojiId]           INT             NOT NULL,
    [CreatedAt]         DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Reaction] PRIMARY KEY CLUSTERED ([ReactionId]),
    CONSTRAINT [FK_Reaction_Stream] FOREIGN KEY ([StreamId]) REFERENCES [dbo].[Stream]([StreamId]),
    CONSTRAINT [FK_Reaction_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId]),
    CONSTRAINT [FK_Reaction_Emoji] FOREIGN KEY ([EmojiId]) REFERENCES [dbo].[Emoji]([EmojiId]),
    CONSTRAINT [UQ_Reaction_Stream_User_Emoji] UNIQUE ([StreamId], [UserId], [EmojiId])
);

CREATE TABLE [dbo].[StreamView] (
    [StreamViewId]      INT             IDENTITY(1,1) NOT NULL,
    [StreamId]          INT             NOT NULL,
    [UserId]            INT             NULL,
    [IpHash]            VARBINARY(32)   NOT NULL,
    [ViewedAt]          DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_StreamView] PRIMARY KEY CLUSTERED ([StreamViewId]),
    CONSTRAINT [FK_StreamView_Stream] FOREIGN KEY ([StreamId]) REFERENCES [dbo].[Stream]([StreamId]),
    CONSTRAINT [FK_StreamView_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE INDEX [IX_StreamView_ViewedAt] ON [dbo].[StreamView] ([ViewedAt] DESC);
CREATE INDEX [IX_StreamView_StreamId] ON [dbo].[StreamView] ([StreamId]);

------------------------------------------------------------------
-- Audit / observability
------------------------------------------------------------------

CREATE TABLE [dbo].[AuditHistory] (
    [AuditHistoryId]    INT             IDENTITY(1,1) NOT NULL,
    [UserId]            INT             NULL,
    [Entity]            VARCHAR(50)     NOT NULL,
    [EntityId]          INT             NOT NULL,
    [Action]            VARCHAR(20)     NOT NULL,
    [Diff]              NVARCHAR(MAX)   NULL,
    [CreatedAt]         DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_AuditHistory] PRIMARY KEY CLUSTERED ([AuditHistoryId]),
    CONSTRAINT [FK_AuditHistory_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE INDEX [IX_AuditHistory_Entity] ON [dbo].[AuditHistory] ([Entity], [EntityId]);

CREATE TABLE [dbo].[Deletion] (
    [DeletionId]        INT             IDENTITY(1,1) NOT NULL,
    [Entity]            VARCHAR(50)     NOT NULL,
    [EntityId]          INT             NOT NULL,
    [UserId]            INT             NULL,
    [DeletedAt]         DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_Deletion] PRIMARY KEY CLUSTERED ([DeletionId]),
    CONSTRAINT [FK_Deletion_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE TABLE [dbo].[ApiCallLog] (
    [ApiCallLogId]      BIGINT          IDENTITY(1,1) NOT NULL,
    [Method]            VARCHAR(10)     NOT NULL,
    [Path]              VARCHAR(500)    NOT NULL,
    [QueryString]       VARCHAR(2000)   NULL,
    [RequestBody]       NVARCHAR(MAX)   NULL,
    [ResponseStatus]    INT             NOT NULL,
    [ResponseBody]      NVARCHAR(MAX)   NULL,
    [UserId]            INT             NULL,
    [IpHash]            VARBINARY(32)   NOT NULL,
    [ServiceKeyHash]    VARBINARY(32)   NULL,
    [DurationMs]        INT             NOT NULL,
    [CalledAt]          DATETIMEOFFSET(7) NOT NULL,
    [RelatedAuditId]    INT             NULL,
    CONSTRAINT [PK_ApiCallLog] PRIMARY KEY CLUSTERED ([ApiCallLogId]),
    CONSTRAINT [FK_ApiCallLog_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId]),
    CONSTRAINT [FK_ApiCallLog_AuditHistory] FOREIGN KEY ([RelatedAuditId]) REFERENCES [dbo].[AuditHistory]([AuditHistoryId])
);

CREATE INDEX [IX_ApiCallLog_CalledAt] ON [dbo].[ApiCallLog] ([CalledAt] DESC);
CREATE INDEX [IX_ApiCallLog_UserId] ON [dbo].[ApiCallLog] ([UserId]);
CREATE INDEX [IX_ApiCallLog_Path] ON [dbo].[ApiCallLog] ([Path]);

CREATE TABLE [dbo].[DownloaderEvent] (
    [DownloaderEventId] BIGINT          IDENTITY(1,1) NOT NULL,
    [Stage]             VARCHAR(30)     NOT NULL,
    [TwitchVodId]       VARCHAR(50)     NULL,
    [Success]           BIT             NOT NULL,
    [DurationMs]        INT             NULL,
    [Payload]           NVARCHAR(MAX)   NULL,
    [Message]           NVARCHAR(1000)  NULL,
    [OccurredAt]        DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_DownloaderEvent] PRIMARY KEY CLUSTERED ([DownloaderEventId])
);

CREATE INDEX [IX_DownloaderEvent_OccurredAt] ON [dbo].[DownloaderEvent] ([OccurredAt] DESC);
CREATE INDEX [IX_DownloaderEvent_Stage] ON [dbo].[DownloaderEvent] ([Stage]);

CREATE TABLE [dbo].[WebsiteEvent] (
    [WebsiteEventId]    BIGINT          IDENTITY(1,1) NOT NULL,
    [Action]            VARCHAR(50)     NOT NULL,
    [Path]              VARCHAR(500)    NULL,
    [UserId]            INT             NULL,
    [IpHash]            VARBINARY(32)   NOT NULL,
    [Detail]            NVARCHAR(MAX)   NULL,
    [OccurredAt]        DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_WebsiteEvent] PRIMARY KEY CLUSTERED ([WebsiteEventId]),
    CONSTRAINT [FK_WebsiteEvent_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE INDEX [IX_WebsiteEvent_OccurredAt] ON [dbo].[WebsiteEvent] ([OccurredAt] DESC);
CREATE INDEX [IX_WebsiteEvent_Action] ON [dbo].[WebsiteEvent] ([Action]);
CREATE INDEX [IX_WebsiteEvent_UserId] ON [dbo].[WebsiteEvent] ([UserId]);

CREATE TABLE [dbo].[ErrorLog] (
    [ErrorLogId]            BIGINT          IDENTITY(1,1) NOT NULL,
    [Source]                VARCHAR(20)     NOT NULL,
    [ExceptionType]         VARCHAR(200)    NOT NULL,
    [Message]               NVARCHAR(2000)  NOT NULL,
    [StackTrace]            NVARCHAR(MAX)   NULL,
    [Context]               NVARCHAR(MAX)   NULL,
    [StatusId]              INT             NOT NULL,
    [GitHubIssueUrl]        VARCHAR(500)    NULL,
    [AddressedByUserId]     INT             NULL,
    [AddressedAt]           DATETIMEOFFSET(7) NULL,
    [Notes]                 NVARCHAR(MAX)   NULL,
    [OccurredAt]            DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_ErrorLog] PRIMARY KEY CLUSTERED ([ErrorLogId]),
    CONSTRAINT [FK_ErrorLog_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[ErrorStatus]([ErrorStatusId]),
    CONSTRAINT [FK_ErrorLog_AddressedBy] FOREIGN KEY ([AddressedByUserId]) REFERENCES [dbo].[User]([UserId])
);

CREATE INDEX [IX_ErrorLog_OccurredAt] ON [dbo].[ErrorLog] ([OccurredAt] DESC);
CREATE INDEX [IX_ErrorLog_StatusId] ON [dbo].[ErrorLog] ([StatusId]);
CREATE INDEX [IX_ErrorLog_Source] ON [dbo].[ErrorLog] ([Source]);
