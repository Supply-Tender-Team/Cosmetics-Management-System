/*
  Demo data fix: only Evaluation Committee accounts sit on an evaluation committee (and see the EVALUATION menu).

  Why: the demo seeders put Sourcing Manager accounts (manager@lumiere.local "Nour Manager", lab.manager@demo.local) on the 4th committee seat of
  the demo tenders, and gave lab.manager the Evaluation Committee role as well. In the real flow only Evaluation Committee accounts are seated
  (Admin > Evaluation committee), the Manager only reviews the finished report. This script moves the demo seats to an Evaluation Committee demo
  account and removes the Evaluation Committee role from every Sourcing Manager account.

  What it does, in one transaction (all or nothing):
    1. Every committee seat held by a Sourcing Manager account goes to an active Evaluation Committee demo account (@lumiere.local / @demo.local,
       not a Sourcing Manager, not already seated on that tender). demo-sourcing-representative@lumiere.local is preferred.
    2. The scores, Ready confirmations and votes the manager had saved on those seats move to the new account, so the demo tenders keep their results.
    3. The "evaluation brief shared" notifications are copied to the new account, so it sees the tender like a real member.
    4. Accounts that have both the Sourcing Manager and Evaluation Committee roles lose the Evaluation Committee role.
    5. The manager accounts lose the "evaluation brief shared" notifications of tenders they hold no seat on (their link would only give a 404).
    6. One audit row is written. Safe to run again: with nothing left to move it does nothing.

  Run it on the database you use (change the name if yours differs):
    sqlcmd -S localhost -d TenderSystemDb -E -C -I -i "Data\Seed\fix-manager-evaluation-seats.sql"

  A fresh run of the demo seeders (new empty database) puts the manager back on the seats: run this script again after seeding.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Accounts that have any role other than Evaluation Committee (Requester, Sourcing Officer, Sourcing Manager, Top Committee, Admin...)
SELECT DISTINCT ur.UserId INTO #notEvaluators
FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name <> 'EvaluationCommittee';

-- 1. The seats that must change
SELECT m.Id AS MemberId, m.TenderId, m.MemberRole, m.UserId AS OldUserId, CAST(NULL AS nvarchar(450)) AS NewUserId
INTO #seat
FROM CommitteeMembers m
WHERE m.UserId IN (SELECT UserId FROM #notEvaluators);

UPDATE s
SET NewUserId = (
    SELECT TOP 1 u.Id
    FROM AspNetUsers u
    JOIN AspNetUserRoles ur ON ur.UserId = u.Id
    JOIN AspNetRoles r ON r.Id = ur.RoleId AND r.Name = 'EvaluationCommittee'
    WHERE u.IsActive = 1
      AND (u.Email LIKE '%@lumiere.local' OR u.Email LIKE '%@demo.local')
      AND u.Id NOT IN (SELECT UserId FROM #notEvaluators)
      AND NOT EXISTS (SELECT 1 FROM CommitteeMembers m2 WHERE m2.TenderId = s.TenderId AND m2.UserId = u.Id)
    ORDER BY CASE WHEN s.MemberRole = 'Department' AND u.Email = 'demo-department-manager@lumiere.local' THEN 0
                  WHEN s.MemberRole <> 'Department' AND u.Email = 'demo-sourcing-representative@lumiere.local' THEN 0
                  ELSE 1 END, u.Email)
FROM #seat s;

IF EXISTS (SELECT 1 FROM #seat WHERE NewUserId IS NULL)
BEGIN
    RAISERROR('No active Evaluation Committee demo account is free for some tenders. Nothing was changed.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

DECLARE @seats int = (SELECT COUNT(*) FROM #seat);

-- 2. Move the seat and everything saved on it
UPDATE te SET EvaluatorUserId = s.NewUserId
FROM TechnicalEvaluations te
JOIN Bids b ON b.Id = te.BidId
JOIN #seat s ON s.TenderId = b.TenderId AND s.OldUserId = te.EvaluatorUserId;

UPDATE fe SET EvaluatorUserId = s.NewUserId
FROM FinancialEvaluations fe
JOIN Bids b ON b.Id = fe.BidId
JOIN #seat s ON s.TenderId = b.TenderId AND s.OldUserId = fe.EvaluatorUserId;

UPDATE sub SET EvaluatorUserId = s.NewUserId
FROM EvaluationCommitteeSubmissions sub
JOIN #seat s ON s.TenderId = sub.TenderId AND s.OldUserId = sub.EvaluatorUserId;

UPDATE m SET UserId = s.NewUserId
FROM CommitteeMembers m
JOIN #seat s ON s.MemberId = m.Id;

-- 3. The new member gets the same "brief shared" notification the old one had
INSERT INTO Notifications (UserId, Type, Title, Message, Link, IsRead, CreatedAt)
SELECT s.NewUserId, n.Type, n.Title, n.Message, n.Link, 0, n.CreatedAt
FROM Notifications n
JOIN #seat s ON s.OldUserId = n.UserId AND n.Type = 'TenderEvaluation' AND n.Link = '/Sourcing/CommitteeBrief/Index/' + CAST(s.TenderId AS varchar(12))
WHERE NOT EXISTS (SELECT 1 FROM Notifications x WHERE x.UserId = s.NewUserId AND x.Type = n.Type AND x.Link = n.Link);

-- 4. No account may have the Evaluation Committee role together with another role
DELETE ur
FROM AspNetUserRoles ur
JOIN AspNetRoles r ON r.Id = ur.RoleId AND r.Name = 'EvaluationCommittee'
WHERE ur.UserId IN (SELECT UserId FROM #notEvaluators);
DECLARE @roles int = @@ROWCOUNT;

-- 5. These accounts keep no "evaluation brief shared" notification for a tender they hold no seat on: the link would only give a 404.
--    (This also cleans databases where the seats were moved by an earlier version of this script.)
DELETE n
FROM Notifications n
WHERE n.Type = 'TenderEvaluation'
  AND n.Link LIKE '/Sourcing/CommitteeBrief/Index/%'
  AND n.UserId IN (SELECT UserId FROM #notEvaluators)
  AND NOT EXISTS (SELECT 1 FROM CommitteeMembers m
                  WHERE m.UserId = n.UserId AND n.Link = '/Sourcing/CommitteeBrief/Index/' + CAST(m.TenderId AS varchar(12)));
DECLARE @notes int = @@ROWCOUNT;

-- 6. Audit
IF @seats > 0 OR @roles > 0 OR @notes > 0
    INSERT INTO AuditLogs (UserId, Action, EntityName, EntityId, NewValues, IpAddress, CreatedAt)
    VALUES (NULL, 'DemoDataFixed', 'CommitteeMember', 'committee-seats',
            CONCAT('Demo seeds: ', @seats, ' committee seat(s) moved from accounts that are not Evaluation Committee accounts (Requester, Manager...) to Evaluation Committee accounts; ',
                   @roles, ' Evaluation Committee role(s) removed from accounts with another role; ',
                   @notes, ' evaluation brief notification(s) removed from those accounts.'),
            '', SYSUTCDATETIME());

COMMIT TRANSACTION;

SELECT CONCAT(@seats, ' seat(s) moved, ', @roles, ' role(s) removed, ', @notes, ' notification(s) removed.') AS result;
