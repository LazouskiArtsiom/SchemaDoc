-- ============ DEV (most features) ============
USE SchemaDoc_Dev;
GO

CREATE INDEX IX_Users_Email_Status ON app.Users(Email, IsActive);
CREATE INDEX IX_Tasks_Project_Priority ON app.Tasks(ProjectId, Priority DESC);
CREATE UNIQUE INDEX UQ_Users_Email ON app.Users(Email);
CREATE INDEX IX_Tasks_Status ON app.Tasks(Status) INCLUDE (Priority, DueDate);
CREATE INDEX IX_AuditLog_Created ON app.AuditLog(CreatedAt DESC);

ALTER TABLE app.Tasks ADD CONSTRAINT CK_Tasks_Priority CHECK (Priority >= 0 AND Priority <= 10);
ALTER TABLE app.Tasks ADD CONSTRAINT CK_Tasks_EstimatedHours CHECK (EstimatedHours IS NULL OR EstimatedHours > 0);
GO

CREATE TRIGGER trg_Tasks_UpdatedAt ON app.Tasks AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
END;
GO

CREATE TRIGGER trg_AuditLog_Prevent_Update ON app.AuditLog INSTEAD OF UPDATE AS
BEGIN
    RAISERROR('AuditLog is immutable', 16, 1);
END;
GO

-- ============ STAGING (middle) ============
USE SchemaDoc_Staging;
GO

-- Same index NAME but DIFFERENT columns — this is the key diff case!
CREATE INDEX IX_Users_Email_Status ON app.Users(Email);  -- Only Email, not (Email, IsActive)
CREATE INDEX IX_Tasks_Project_Priority ON app.Tasks(ProjectId);  -- Only ProjectId
CREATE UNIQUE INDEX UQ_Users_Email ON app.Users(Email);

-- No CK_Tasks_EstimatedHours (column doesn't exist in staging)
ALTER TABLE app.Tasks ADD CONSTRAINT CK_Tasks_Priority CHECK (Priority >= 0 AND Priority <= 10);
GO

CREATE TRIGGER trg_Tasks_UpdatedAt ON app.Tasks AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
END;
GO

-- ============ PROD (least) ============
USE SchemaDoc_Prod;
GO

-- Minimal: only unique constraint on email
CREATE UNIQUE INDEX UQ_Users_Email ON app.Users(Email);
GO
