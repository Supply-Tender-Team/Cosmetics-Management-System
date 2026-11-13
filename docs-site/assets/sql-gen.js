// Builds the SQL Server script from data.js (so the diagrams and the script can never disagree).
(function (root) {
  function gen(DB) {
    const L = [];
    const out = s => L.push(s);
    const q = n => `[${n}]`;

    out('/* =====================================================================');
    out('   Sourcing & Tender Management System - SQL Server reference script');
    out('   Generated from the same design used by the Schema and ER diagrams.');
    out('');
    out('   NOTE: In the real project the database is created by EF Core migrations');
    out('   (dotnet ef database update). Use this script to READ and discuss the');
    out('   design, or to build a quick playground database.');
    out('   ===================================================================== */');
    out('');
    out('IF DB_ID(N\'TenderSystemDb\') IS NULL CREATE DATABASE TenderSystemDb;');
    out('GO');
    out('USE TenderSystemDb;');
    out('GO');
    out('');

    // ---- tables ----
    for (const [key, dom] of Object.entries(DB.domains)) {
      out('/* ---------------------------------------------------------------');
      out(`   ${dom.label.toUpperCase()}`);
      out('   --------------------------------------------------------------- */');
      for (const t of DB.tables.filter(x => x.domain === key)) {
        out(`-- ${t.desc}`);
        out(`CREATE TABLE dbo.${q(t.name)} (`);
        const pks = t.cols.filter(c => c.pk);
        const lines = [];
        for (const c of t.cols) {
          let l = `    ${q(c.name).padEnd(24)} ${c.type}`;
          if (c.pk && pks.length === 1 && /^(int|bigint)$/.test(c.type)) l += ' IDENTITY(1,1)';
          l += c.nullable ? ' NULL' : ' NOT NULL';
          let def = c.def;
          if (def == null && !c.nullable) {
            if (c.name === 'CreatedAt') def = 'SYSUTCDATETIME()';
            else if (c.type === 'bit') def = c.name === 'IsActive' ? '1' : '0';
          }
          if (def != null) l += ` CONSTRAINT DF_${t.name}_${c.name} DEFAULT ${def}`;
          lines.push(l);
        }
        lines.push(`    CONSTRAINT PK_${t.name} PRIMARY KEY (${pks.map(c => q(c.name)).join(', ')})`);
        for (const c of t.cols.filter(c => c.uq && !c.pk))
          lines.push(`    CONSTRAINT UQ_${t.name}_${c.name} UNIQUE (${q(c.name)})`);
        for (const c of t.cols.filter(c => c.ck))
          lines.push(`    CONSTRAINT CK_${t.name}_${c.name} CHECK (${q(c.name)} IN (${c.ck.map(v => `N'${v}'`).join(', ')}))`);
        out(lines.join(',\n'));
        out(');');
        out('GO');
        out('');
      }
    }

    // ---- foreign keys ----
    out('/* ---------------------------------------------------------------');
    out('   FOREIGN KEYS (added after all tables exist)');
    out('   --------------------------------------------------------------- */');
    for (const r of DB.relations) {
      out(`ALTER TABLE dbo.${q(r.child)} ADD CONSTRAINT FK_${r.child}_${r.childCol}`);
      out(`    FOREIGN KEY (${q(r.childCol)}) REFERENCES dbo.${q(r.parent)} (${q(r.parentCol)});`);
    }
    out('GO');
    out('');

    // ---- indexes ----
    out('/* ---------------------------------------------------------------');
    out('   INDEXES on foreign keys (faster joins and filters)');
    out('   --------------------------------------------------------------- */');
    for (const r of DB.relations) {
      const t = DB.byName[r.child];
      const col = t.cols.find(c => c.name === r.childCol);
      if (col.uq || col.pk) continue;
      out(`CREATE INDEX IX_${r.child}_${r.childCol} ON dbo.${q(r.child)} (${q(r.childCol)});`);
    }
    out('GO');
    out('');

    // ---- seed ----
    out('/* ---------------------------------------------------------------');
    out('   SEED DATA - roles and departments');
    out('   --------------------------------------------------------------- */');
    const roles = ['Admin', 'Requester', 'SourcingOfficer', 'EvaluationCommittee', 'SourcingManager', 'TopCommittee', 'Supplier'];
    out('INSERT INTO dbo.[AspNetRoles] ([Id], [Name], [NormalizedName]) VALUES');
    out(roles.map(r => `    (CONVERT(nvarchar(450), NEWID()), N'${r}', N'${r.toUpperCase()}')`).join(',\n') + ';');
    out('');
    out('INSERT INTO dbo.[Departments] ([Name], [Code]) VALUES');
    out(['Production|PRD', 'Packaging|PKG', 'Research & Development|RND', 'Marketing|MKT', 'Quality Control|QC', 'Finance|FIN', 'IT|IT', 'Sourcing|SRC']
      .map(s => { const [n, c] = s.split('|'); return `    (N'${n}', N'${c}')`; }).join(',\n') + ';');
    out('GO');
    out('');

    // ---- handy queries ----
    out('/* ---------------------------------------------------------------');
    out('   EXAMPLE QUERIES (good for the live demo)');
    out('   --------------------------------------------------------------- */');
    out('-- 1) Bids of one tender, cheapest first');
    out('SELECT s.CompanyName, b.TotalPrice, b.Currency, b.DeliveryDays, b.Status');
    out('FROM dbo.Bids b JOIN dbo.SupplierProfiles s ON s.Id = b.SupplierProfileId');
    out('WHERE b.TenderId = 1 AND b.Status <> N\'Draft\'');
    out('ORDER BY b.TotalPrice;');
    out('');
    out('-- 2) Tenders with zero bids');
    out('SELECT t.TenderNumber, t.Title');
    out('FROM dbo.Tenders t');
    out('WHERE NOT EXISTS (SELECT 1 FROM dbo.Bids b WHERE b.TenderId = t.Id AND b.Status <> N\'Draft\');');
    out('');
    out('-- 3) Users who finished BOTH biometric registrations');
    out('SELECT u.Email');
    out('FROM dbo.AspNetUsers u');
    out('WHERE EXISTS (SELECT 1 FROM dbo.BiometricCredentials c WHERE c.UserId = u.Id AND c.IsRevoked = 0)');
    out('  AND EXISTS (SELECT 1 FROM dbo.FaceEnrollments f WHERE f.UserId = u.Id AND f.IsActive = 1);');
    out('');
    out('-- 4) Read the AI answer stored as JSON');
    out('SELECT BidId, JSON_VALUE(ResponseJson, \'$.recommendation\') AS Recommendation');
    out('FROM dbo.AiBidAnalyses WHERE ISJSON(ResponseJson) = 1;');
    out('');
    out('-- 5) Supplier participation per tender');
    out('SELECT t.TenderNumber, COUNT(DISTINCT i.SupplierProfileId) AS Invited,');
    out('       COUNT(DISTINCT b.SupplierProfileId) AS Bid');
    out('FROM dbo.Tenders t');
    out('LEFT JOIN dbo.Invitations i ON i.TenderId = t.Id');
    out('LEFT JOIN dbo.Bids b ON b.TenderId = t.Id AND b.Status <> N\'Draft\'');
    out('GROUP BY t.TenderNumber;');
    return L.join('\n');
  }
  if (typeof module !== 'undefined') module.exports = { gen }; else root.genSql = gen;
})(typeof window !== 'undefined' ? window : globalThis);
