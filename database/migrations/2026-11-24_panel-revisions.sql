-- =====================================================================================================
-- LENSYNC - upgrade an existing lensync_db to the 2026-11-24 panel revisions (run ONCE, MariaDB/XAMPP).
-- A fresh install does not need this: schema.sql + seed.sql already contain these changes.
--
--   C:\xampp\mysql\bin\mysql.exe -u root --default-character-set=utf8mb4 lensync_db
--       -e "source database/migrations/2026-11-24_panel-revisions.sql"
--
-- 1. Bookings: new status 'Voided' (bill voided in Billing before payment) and closed_reason/by/at for
--    cancelled and voided bookings (the reason is required).
-- 2. Transactions: tax_rate / tax_amount (Total = items - discount + tax).
-- 3. Settings: tax_rate (percent, default 6.00; Settings > Studio).
-- =====================================================================================================

USE lensync_db;
SET NAMES utf8mb4;
START TRANSACTION;

ALTER TABLE bookings
    ADD COLUMN IF NOT EXISTS closed_reason VARCHAR(255) NULL AFTER notes,
    ADD COLUMN IF NOT EXISTS closed_by     INT          NULL AFTER closed_reason,
    ADD COLUMN IF NOT EXISTS closed_at     DATETIME     NULL AFTER closed_by;

ALTER TABLE bookings DROP CONSTRAINT IF EXISTS ck_bookings_status;
ALTER TABLE bookings ADD CONSTRAINT ck_bookings_status CHECK (status IN ('Pending', 'Unpaid', 'Paid', 'Cancelled', 'Voided'));
ALTER TABLE bookings DROP FOREIGN KEY IF EXISTS fk_bookings_closer;
ALTER TABLE bookings ADD CONSTRAINT fk_bookings_closer FOREIGN KEY (closed_by) REFERENCES users (id);

-- Cancellations made before this version kept their reason in the notes as "Cancelled: <reason>".
UPDATE bookings
   SET closed_reason = LEFT(TRIM(SUBSTRING_INDEX(notes, 'Cancelled: ', -1)), 255),
       closed_at     = IFNULL(closed_at, updated_at)
 WHERE status = 'Cancelled' AND closed_reason IS NULL AND notes LIKE '%Cancelled: %';

ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS tax_rate   DECIMAL(5,2)  NOT NULL DEFAULT 0.00 AFTER discount_reason,
    ADD COLUMN IF NOT EXISTS tax_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00 AFTER tax_rate;
ALTER TABLE transactions DROP CONSTRAINT IF EXISTS ck_tx_tax;
ALTER TABLE transactions ADD CONSTRAINT ck_tx_tax CHECK (tax_rate >= 0 AND tax_rate <= 100 AND tax_amount >= 0);

INSERT IGNORE INTO settings (setting_key, setting_value) VALUES ('tax_rate', '6.00');

UPDATE permissions SET label = 'Void an unpaid bill (in Billing, before payment)' WHERE perm_key = 'VOID_SALE';

INSERT INTO audit_logs (user_id, action, entity, details) VALUES
    (NULL, 'MIGRATION', 'system', '2026-11-24 panel revisions: tax, void in Billing, booking close reasons');

COMMIT;
