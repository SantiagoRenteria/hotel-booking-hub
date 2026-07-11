IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709025022_InicialHoteles'
)
BEGIN
    CREATE TABLE [Hoteles] (
        [Id] uniqueidentifier NOT NULL,
        [Nombre] nvarchar(200) NOT NULL,
        [Ciudad] nvarchar(120) NOT NULL,
        [Direccion] nvarchar(300) NOT NULL,
        [Descripcion] nvarchar(2000) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [RowVersion] rowversion NULL,
        [Seq] bigint NOT NULL IDENTITY,
        CONSTRAINT [PK_Hoteles] PRIMARY KEY NONCLUSTERED ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709025022_InicialHoteles'
)
BEGIN
    CREATE UNIQUE CLUSTERED INDEX [IX_Hoteles_Seq] ON [Hoteles] ([Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709025022_InicialHoteles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709025022_InicialHoteles', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709042631_AgregaEliminadoHotel'
)
BEGIN
    ALTER TABLE [Hoteles] ADD [Eliminado] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709042631_AgregaEliminadoHotel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709042631_AgregaEliminadoHotel', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709113506_AgregaHabitaciones'
)
BEGIN
    CREATE TABLE [Habitaciones] (
        [Id] uniqueidentifier NOT NULL,
        [HotelId] uniqueidentifier NOT NULL,
        [Tipo] nvarchar(100) NOT NULL,
        [CostoBase] decimal(18,2) NOT NULL,
        [Impuestos] decimal(18,2) NOT NULL,
        [Ubicacion] nvarchar(200) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [RowVersion] rowversion NULL,
        [Seq] bigint NOT NULL IDENTITY,
        CONSTRAINT [PK_Habitaciones] PRIMARY KEY NONCLUSTERED ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709113506_AgregaHabitaciones'
)
BEGIN
    CREATE INDEX [IX_Habitaciones_HotelId] ON [Habitaciones] ([HotelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709113506_AgregaHabitaciones'
)
BEGIN
    CREATE UNIQUE CLUSTERED INDEX [IX_Habitaciones_Seq] ON [Habitaciones] ([Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709113506_AgregaHabitaciones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709113506_AgregaHabitaciones', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709132200_AgregaVersionYOutboxHabitaciones'
)
BEGIN
    ALTER TABLE [Habitaciones] ADD [Version] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709132200_AgregaVersionYOutboxHabitaciones'
)
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Seq] bigint NOT NULL IDENTITY,
        [MessageId] uniqueidentifier NOT NULL,
        [AggregateId] uniqueidentifier NOT NULL,
        [Type] nvarchar(200) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [Estado] nvarchar(32) NOT NULL,
        [Intentos] int NOT NULL,
        [ReclamadoEn] datetimeoffset NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Seq])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709132200_AgregaVersionYOutboxHabitaciones'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_Estado_Seq] ON [OutboxMessages] ([Estado], [Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709132200_AgregaVersionYOutboxHabitaciones'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OutboxMessages_MessageId] ON [OutboxMessages] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709132200_AgregaVersionYOutboxHabitaciones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709132200_AgregaVersionYOutboxHabitaciones', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709144649_AgregaCapacidadHabitacion'
)
BEGIN
    ALTER TABLE [Habitaciones] ADD [Capacidad] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709144649_AgregaCapacidadHabitacion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709144649_AgregaCapacidadHabitacion', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709164759_AgregaVersionHotel'
)
BEGIN
    ALTER TABLE [Hoteles] ADD [Version] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709164759_AgregaVersionHotel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709164759_AgregaVersionHotel', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710025456_AislamientoAgentePropietarioHotel'
)
BEGIN
    ALTER TABLE [Hoteles] ADD [AgentePropietario] nvarchar(320) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710025456_AislamientoAgentePropietarioHotel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260710025456_AislamientoAgentePropietarioHotel', N'10.0.9');
END;

COMMIT;
GO

