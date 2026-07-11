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
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
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
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Seq])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    CREATE TABLE [Reservas] (
        [Id] uniqueidentifier NOT NULL,
        [HabitacionId] uniqueidentifier NOT NULL,
        [Entrada] date NOT NULL,
        [Salida] date NOT NULL,
        [Estado] nvarchar(32) NOT NULL,
        [RowVersion] rowversion NULL,
        [Seq] bigint NOT NULL IDENTITY,
        CONSTRAINT [PK_Reservas] PRIMARY KEY NONCLUSTERED ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    CREATE TABLE [NochesHabitacion] (
        [HabitacionId] uniqueidentifier NOT NULL,
        [Noche] date NOT NULL,
        [ReservaId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_NochesHabitacion] PRIMARY KEY ([HabitacionId], [Noche]),
        CONSTRAINT [FK_NochesHabitacion_Reservas_ReservaId] FOREIGN KEY ([ReservaId]) REFERENCES [Reservas] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    CREATE INDEX [IX_NochesHabitacion_ReservaId] ON [NochesHabitacion] ([ReservaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OutboxMessages_MessageId] ON [OutboxMessages] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    CREATE UNIQUE CLUSTERED INDEX [IX_Reservas_Seq] ON [Reservas] ([Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709002320_InicialReservas'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709002320_InicialReservas', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709013019_OutboxLeaseYPolling'
)
BEGIN
    ALTER TABLE [OutboxMessages] ADD [ReclamadoEn] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709013019_OutboxLeaseYPolling'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_Estado_Seq] ON [OutboxMessages] ([Estado], [Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709013019_OutboxLeaseYPolling'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709013019_OutboxLeaseYPolling', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709151539_AgregaProyeccionEInbox'
)
BEGIN
    CREATE TABLE [MensajesProcesados] (
        [MessageId] uniqueidentifier NOT NULL,
        [ProcesadoEn] datetimeoffset NOT NULL,
        CONSTRAINT [PK_MensajesProcesados] PRIMARY KEY ([MessageId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709151539_AgregaProyeccionEInbox'
)
BEGIN
    CREATE TABLE [ProyeccionHabitacion] (
        [HabitacionId] uniqueidentifier NOT NULL,
        [HotelId] uniqueidentifier NULL,
        [Ciudad] nvarchar(120) NULL,
        [Tipo] nvarchar(100) NULL,
        [Ubicacion] nvarchar(200) NULL,
        [Capacidad] int NULL,
        [VersionEstatico] int NULL,
        [CostoBase] decimal(18,2) NULL,
        [Impuestos] decimal(18,2) NULL,
        [VersionPrecio] int NOT NULL,
        [Estado] nvarchar(20) NULL,
        [VersionEstado] int NOT NULL,
        CONSTRAINT [PK_ProyeccionHabitacion] PRIMARY KEY ([HabitacionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709151539_AgregaProyeccionEInbox'
)
BEGIN
    CREATE INDEX [IX_ProyeccionHabitacion_Ciudad] ON [ProyeccionHabitacion] ([Ciudad]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709151539_AgregaProyeccionEInbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709151539_AgregaProyeccionEInbox', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709164809_AgregaProyeccionHotelEstado'
)
BEGIN
    CREATE TABLE [ProyeccionHotelEstado] (
        [HotelId] uniqueidentifier NOT NULL,
        [Activo] bit NOT NULL,
        [VersionEstado] int NOT NULL,
        CONSTRAINT [PK_ProyeccionHotelEstado] PRIMARY KEY ([HotelId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709164809_AgregaProyeccionHotelEstado'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709164809_AgregaProyeccionHotelEstado', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    ALTER TABLE [Reservas] ADD [AgenteEmail] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    ALTER TABLE [Reservas] ADD [ContactoNombreCompleto] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    ALTER TABLE [Reservas] ADD [ContactoTelefono] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    ALTER TABLE [Reservas] ADD [PrecioTotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    CREATE TABLE [ReservaHuespedes] (
        [ReservaId] uniqueidentifier NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Nombres] nvarchar(120) NOT NULL,
        [Apellidos] nvarchar(120) NOT NULL,
        [FechaNacimiento] date NOT NULL,
        [Genero] nvarchar(40) NOT NULL,
        [DocumentoTipo] nvarchar(40) NOT NULL,
        [DocumentoNumero] nvarchar(60) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Telefono] nvarchar(40) NOT NULL,
        CONSTRAINT [PK_ReservaHuespedes] PRIMARY KEY ([ReservaId], [Id]),
        CONSTRAINT [FK_ReservaHuespedes_Reservas_ReservaId] FOREIGN KEY ([ReservaId]) REFERENCES [Reservas] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709172417_PersisteDatosReserva'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709172417_PersisteDatosReserva', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionFechaSolicitud] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionIniciadaPor] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionMotivoCategoria] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionMotivoDetalle] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionPenalidadPorcentaje] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182050_AgregaSolicitudCancelacion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709182050_AgregaSolicitudCancelacion', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionFechaResolucion] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionMotivoResolucion] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionPenalidadAplicadaPorcentaje] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionPenalidadFueOverride] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionResueltaPor] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    ALTER TABLE [Reservas] ADD [CancelacionResultado] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185445_AgregaResolucionCancelacion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709185445_AgregaResolucionCancelacion', N'10.0.9');
END;

COMMIT;
GO

