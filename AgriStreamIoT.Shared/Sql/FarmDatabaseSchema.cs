using Dapper;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>
/// The relational part of AgriStream IoT (Azure SQL Database):
///   Users               – logins with the Farmer / Agronomist / Admin role
///   Fields              – the farm's fields (crop, area, irrigation threshold)
///   Devices             – the MASTER DEVICE REGISTER with each device's operational status
///   DeviceStatusHistory – every status change (who/what changed it and why)
/// The script is idempotent, so the web app runs it on start-up against an empty database.
/// The same script is in infra/sql/01-schema.sql for the Azure portal Query editor.
/// </summary>
public static class FarmDatabaseSchema
{
    public static readonly IReadOnlyList<string> Statements = new[]
    {
        """
        IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        CREATE TABLE dbo.Users (
            UserId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
            FullName      NVARCHAR(120) NOT NULL,
            Email         NVARCHAR(254) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
            PhoneNumber   NVARCHAR(20)  NULL,
            PasswordHash  NVARCHAR(200) NOT NULL,
            Role          NVARCHAR(20)  NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN (N'Farmer', N'Agronomist', N'Admin')),
            IsActive      BIT           NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
            CreatedAtUtc  DATETIME2(0)  NOT NULL CONSTRAINT DF_Users_Created DEFAULT (SYSUTCDATETIME()),
            LastLoginUtc  DATETIME2(0)  NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.Fields', N'U') IS NULL
        CREATE TABLE dbo.Fields (
            FieldId               NVARCHAR(10)  NOT NULL CONSTRAINT PK_Fields PRIMARY KEY,
            Name                  NVARCHAR(100) NOT NULL,
            Crop                  NVARCHAR(60)  NOT NULL,
            AreaHectares          DECIMAL(8,2)  NOT NULL,
            SoilType              NVARCHAR(40)  NOT NULL,
            IrrigationMethod      NVARCHAR(40)  NOT NULL,
            MoistureThresholdPct  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Fields_Threshold DEFAULT (25),
            FlowRateLitresPerMin  DECIMAL(10,2) NOT NULL CONSTRAINT DF_Fields_Flow DEFAULT (500),
            Latitude              DECIMAL(9,6)  NULL,
            Longitude             DECIMAL(9,6)  NULL,
            Notes                 NVARCHAR(400) NULL,
            UpdatedBy             NVARCHAR(254) NULL,
            UpdatedAtUtc          DATETIME2(0)  NOT NULL CONSTRAINT DF_Fields_Updated DEFAULT (SYSUTCDATETIME())
        );
        """,
        """
        IF OBJECT_ID(N'dbo.Devices', N'U') IS NULL
        CREATE TABLE dbo.Devices (
            DeviceId             NVARCHAR(20)  NOT NULL CONSTRAINT PK_Devices PRIMARY KEY,
            DeviceName           NVARCHAR(100) NOT NULL,
            DeviceType           NVARCHAR(30)  NOT NULL,
            FieldId              NVARCHAR(10)  NULL CONSTRAINT FK_Devices_Fields REFERENCES dbo.Fields(FieldId),
            SerialNumber         NVARCHAR(40)  NOT NULL CONSTRAINT UQ_Devices_Serial UNIQUE,
            FirmwareVersion      NVARCHAR(20)  NOT NULL,
            OperationalStatus    NVARCHAR(20)  NOT NULL CONSTRAINT DF_Devices_Status DEFAULT (N'Online')
                                 CONSTRAINT CK_Devices_Status CHECK (OperationalStatus IN (N'Online', N'Irrigating', N'Offline', N'Maintenance', N'Fault')),
            BatteryPct           INT           NULL,
            InstalledOn          DATE          NOT NULL,
            LastSeenUtc          DATETIME2(0)  NULL,
            LastCommandId        NVARCHAR(40)  NULL,
            LastCommandAction    NVARCHAR(10)  NULL,
            LastCommandAtUtc     DATETIME2(0)  NULL,
            IrrigationEndsAtUtc  DATETIME2(0)  NULL,
            StatusUpdatedBy      NVARCHAR(254) NULL,
            StatusUpdatedAtUtc   DATETIME2(0)  NOT NULL CONSTRAINT DF_Devices_StatusUpdated DEFAULT (SYSUTCDATETIME()),
            Notes                NVARCHAR(400) NULL
        );
        """,
        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Devices_Field' AND object_id = OBJECT_ID(N'dbo.Devices'))
        CREATE INDEX IX_Devices_Field ON dbo.Devices (FieldId, DeviceType);
        """,
        """
        IF OBJECT_ID(N'dbo.DeviceStatusHistory', N'U') IS NULL
        CREATE TABLE dbo.DeviceStatusHistory (
            HistoryId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeviceStatusHistory PRIMARY KEY,
            DeviceId      NVARCHAR(20)  NOT NULL CONSTRAINT FK_History_Devices REFERENCES dbo.Devices(DeviceId) ON DELETE CASCADE,
            OldStatus     NVARCHAR(20)  NULL,
            NewStatus     NVARCHAR(20)  NOT NULL,
            CommandId     NVARCHAR(40)  NULL,
            ChangedBy     NVARCHAR(254) NOT NULL,
            Reason        NVARCHAR(300) NULL,
            ChangedAtUtc  DATETIME2(0)  NOT NULL CONSTRAINT DF_History_Changed DEFAULT (SYSUTCDATETIME())
        );
        """,
        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_History_Device' AND object_id = OBJECT_ID(N'dbo.DeviceStatusHistory'))
        CREATE INDEX IX_History_Device ON dbo.DeviceStatusHistory (DeviceId, ChangedAtUtc DESC);
        """
    };

    /// <summary>Creates any missing tables. Each statement runs on its own so foreign keys resolve.</summary>
    public static async Task EnsureCreatedAsync(SqlConnectionFactory database, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, commandTimeout: 60, cancellationToken: cancellationToken));
        }
    }
}
