-- ════════════════════════════════════════════════════════════════════
-- Multi-environment proc/function seed for migration testing.
--
-- Installs deliberately-different routines into SchemaDoc_Prod,
-- SchemaDoc_Staging, and SchemaDoc_Dev so every migration direction
-- exercises a mix of add / remove / modify on procs and functions.
--
-- Layout (matches the existing migration-test setup):
--   Prod    — 3 tables (no TaskComments, no UpdatedAt / DueDate / AvatarUrl)
--   Staging — 4 tables (has TaskComments + UpdatedAt + DueDate + AvatarUrl)
--   Dev     — 4 tables (same shape as Staging)
--
-- Routine differences (column-aware so each routine compiles in its DB):
--
--   ROUTINE                          PROD  STAGING  DEV
--   app.usp_GetActiveUsers           same  same     same
--   app.usp_GetTasksByStatus         v1    v2 mod   v3 mod (extra param)
--   app.fn_TaskCountByProject        v1    v2 mod   v2 mod (== Staging)
--   app.usp_GetTaskCommentCount      —     present  present
--   app.fn_UserAvatarOrDefault       —     present  present
--   app.usp_ArchiveProject           —     —        present  (uses UpdatedAt)
--   app.fn_DaysUntilDue              —     —        present  (uses DueDate)
--
-- Idempotent: drops + recreates each object. Safe to re-run.
-- ════════════════════════════════════════════════════════════════════

-- Required for proc/function bodies that reference computed columns or
-- use ANSI semantics. SqlClient defaults are fine; sqlcmd defaults are not.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ════════════════════════════════════════════════════════════════════
-- SchemaDoc_Prod — baseline routines (only references Prod columns)
-- ════════════════════════════════════════════════════════════════════
USE SchemaDoc_Prod;
GO

IF OBJECT_ID('app.usp_GetActiveUsers', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetActiveUsers;
GO
CREATE PROCEDURE app.usp_GetActiveUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.Email, u.Username
    FROM   app.Users u
    WHERE  u.IsActive = 1
    ORDER BY u.Username;
END
GO

IF OBJECT_ID('app.usp_GetTasksByStatus', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetTasksByStatus;
GO
CREATE PROCEDURE app.usp_GetTasksByStatus
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    -- Prod v1: status filter only
    SELECT t.Id, t.Title, t.Status, t.Priority, t.ProjectId, t.AssigneeId
    FROM   app.Tasks t
    WHERE  t.Status = @Status
    ORDER BY t.Priority DESC, t.Id;
END
GO

IF OBJECT_ID('app.fn_TaskCountByProject', 'FN') IS NOT NULL DROP FUNCTION app.fn_TaskCountByProject;
GO
CREATE FUNCTION app.fn_TaskCountByProject (@ProjectId INT)
RETURNS INT
AS
BEGIN
    -- Prod v1: count all tasks (any status)
    DECLARE @c INT;
    SELECT @c = COUNT(*) FROM app.Tasks WHERE ProjectId = @ProjectId;
    RETURN ISNULL(@c, 0);
END
GO

-- ════════════════════════════════════════════════════════════════════
-- SchemaDoc_Staging — Prod baseline + 2 new routines, with v2 changes
-- ════════════════════════════════════════════════════════════════════
USE SchemaDoc_Staging;
GO

IF OBJECT_ID('app.usp_GetActiveUsers', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetActiveUsers;
GO
CREATE PROCEDURE app.usp_GetActiveUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.Email, u.Username
    FROM   app.Users u
    WHERE  u.IsActive = 1
    ORDER BY u.Username;
END
GO

IF OBJECT_ID('app.usp_GetTasksByStatus', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetTasksByStatus;
GO
CREATE PROCEDURE app.usp_GetTasksByStatus
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    -- Staging v2: includes DueDate column (only present on Staging/Dev)
    SELECT t.Id, t.Title, t.Status, t.Priority, t.ProjectId, t.AssigneeId, t.DueDate
    FROM   app.Tasks t
    WHERE  t.Status = @Status
    ORDER BY t.DueDate, t.Priority DESC, t.Id;
END
GO

IF OBJECT_ID('app.fn_TaskCountByProject', 'FN') IS NOT NULL DROP FUNCTION app.fn_TaskCountByProject;
GO
CREATE FUNCTION app.fn_TaskCountByProject (@ProjectId INT)
RETURNS INT
AS
BEGIN
    -- Staging v2: only count non-Done tasks
    DECLARE @c INT;
    SELECT @c = COUNT(*) FROM app.Tasks
     WHERE ProjectId = @ProjectId AND Status <> N'Done';
    RETURN ISNULL(@c, 0);
END
GO

IF OBJECT_ID('app.usp_GetTaskCommentCount', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetTaskCommentCount;
GO
CREATE PROCEDURE app.usp_GetTaskCommentCount
    @TaskId INT,
    @Total INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- TaskComments table only exists on Staging/Dev.
    SELECT @Total = COUNT(*) FROM app.TaskComments WHERE TaskId = @TaskId;
END
GO

IF OBJECT_ID('app.fn_UserAvatarOrDefault', 'FN') IS NOT NULL DROP FUNCTION app.fn_UserAvatarOrDefault;
GO
CREATE FUNCTION app.fn_UserAvatarOrDefault (@UserId INT)
RETURNS NVARCHAR(500)
AS
BEGIN
    -- AvatarUrl column only exists on Staging/Dev.
    DECLARE @url NVARCHAR(500);
    SELECT @url = u.AvatarUrl FROM app.Users u WHERE u.Id = @UserId;
    RETURN ISNULL(@url, N'/static/default-avatar.png');
END
GO

-- ════════════════════════════════════════════════════════════════════
-- SchemaDoc_Dev — Staging routines + 2 more, with v3 changes on tasks proc
-- ════════════════════════════════════════════════════════════════════
USE SchemaDoc_Dev;
GO

IF OBJECT_ID('app.usp_GetActiveUsers', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetActiveUsers;
GO
CREATE PROCEDURE app.usp_GetActiveUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Id, u.Email, u.Username
    FROM   app.Users u
    WHERE  u.IsActive = 1
    ORDER BY u.Username;
END
GO

IF OBJECT_ID('app.usp_GetTasksByStatus', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetTasksByStatus;
GO
CREATE PROCEDURE app.usp_GetTasksByStatus
    @Status      NVARCHAR(50),
    @MinPriority INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    -- Dev v3: extra @MinPriority parameter and a stronger filter
    SELECT t.Id, t.Title, t.Status, t.Priority, t.ProjectId, t.AssigneeId, t.DueDate
    FROM   app.Tasks t
    WHERE  t.Status = @Status
      AND  t.Priority >= @MinPriority
    ORDER BY t.DueDate, t.Priority DESC, t.Id;
END
GO

IF OBJECT_ID('app.fn_TaskCountByProject', 'FN') IS NOT NULL DROP FUNCTION app.fn_TaskCountByProject;
GO
CREATE FUNCTION app.fn_TaskCountByProject (@ProjectId INT)
RETURNS INT
AS
BEGIN
    -- Dev: same as Staging v2
    DECLARE @c INT;
    SELECT @c = COUNT(*) FROM app.Tasks
     WHERE ProjectId = @ProjectId AND Status <> N'Done';
    RETURN ISNULL(@c, 0);
END
GO

IF OBJECT_ID('app.usp_GetTaskCommentCount', 'P') IS NOT NULL DROP PROCEDURE app.usp_GetTaskCommentCount;
GO
CREATE PROCEDURE app.usp_GetTaskCommentCount
    @TaskId INT,
    @Total INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Total = COUNT(*) FROM app.TaskComments WHERE TaskId = @TaskId;
END
GO

IF OBJECT_ID('app.fn_UserAvatarOrDefault', 'FN') IS NOT NULL DROP FUNCTION app.fn_UserAvatarOrDefault;
GO
CREATE FUNCTION app.fn_UserAvatarOrDefault (@UserId INT)
RETURNS NVARCHAR(500)
AS
BEGIN
    DECLARE @url NVARCHAR(500);
    SELECT @url = u.AvatarUrl FROM app.Users u WHERE u.Id = @UserId;
    RETURN ISNULL(@url, N'/static/default-avatar.png');
END
GO

-- Dev-only: archive proc that touches Projects.UpdatedAt
IF OBJECT_ID('app.usp_ArchiveProject', 'P') IS NOT NULL DROP PROCEDURE app.usp_ArchiveProject;
GO
CREATE PROCEDURE app.usp_ArchiveProject
    @ProjectId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE app.Projects
       SET Description = N'[ARCHIVED] ' + ISNULL(Description, N''),
           UpdatedAt   = SYSUTCDATETIME()
     WHERE Id = @ProjectId;
END
GO

-- Dev-only: scalar function depending on Tasks.DueDate
IF OBJECT_ID('app.fn_DaysUntilDue', 'FN') IS NOT NULL DROP FUNCTION app.fn_DaysUntilDue;
GO
CREATE FUNCTION app.fn_DaysUntilDue (@TaskId INT)
RETURNS INT
AS
BEGIN
    DECLARE @due DATE;
    SELECT @due = DueDate FROM app.Tasks WHERE Id = @TaskId;
    IF @due IS NULL RETURN NULL;
    RETURN DATEDIFF(DAY, CAST(SYSUTCDATETIME() AS DATE), @due);
END
GO

PRINT 'Multi-environment routines installed for Prod/Staging/Dev.';
