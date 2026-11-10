-- =====================================================================================================
-- LENSYNC seed data (DEVELOPMENT). Run right after schema.sql:
--   mysql -u root --default-character-set=utf8mb4 -e "source database/seed.sql"
-- Default login: admin / Admin@123 (must change on first login).
-- =====================================================================================================

USE lensync_db;
SET NAMES utf8mb4;
SET time_zone = '+08:00';   -- Asia/Manila

START TRANSACTION;

-- -----------------------------------------------------------------------------------------------------
-- Roles and module permissions
-- -----------------------------------------------------------------------------------------------------
INSERT INTO roles (id, name, description) VALUES
    (1, 'Admin',      'Full access to every module, user accounts and settings'),
    (2, 'Cashier',    'Bookings and billing (Add Service)'),
    (3, 'Accounting', 'Sales monitoring and reports');

INSERT INTO permissions (id, perm_key, label) VALUES
    (1, 'DASHBOARD',   'Dashboard'),
    (3, 'ADD_SERVICE', 'Add Service (bookings, billing and payment)'),
    (4, 'REPORTS',     'Reports'),
    (5, 'SETTINGS',    'Settings (users, roles, pricing, business info)'),
    (6, 'VOID_SALE',   'Void a completed sale');

-- Admin: everything
INSERT INTO role_permissions (role_id, permission_id) SELECT 1, id FROM permissions;
-- Cashier: Dashboard, Add Service (bookings live inside Add Service)
INSERT INTO role_permissions (role_id, permission_id) VALUES (2, 1), (2, 3);
-- Accounting: Dashboard, Reports
INSERT INTO role_permissions (role_id, permission_id) VALUES (3, 1), (3, 4);

-- -----------------------------------------------------------------------------------------------------
-- Default administrator (BCrypt cost 11 hash of "Admin@123")
-- -----------------------------------------------------------------------------------------------------
INSERT INTO users (id, username, password_hash, full_name, email, role_id, is_active, must_change_password) VALUES
    (1, 'admin', '$2a$11$ORIDLNymf8RaXS.d1rZ2k.WpPIHV6Ih2xp1QJGc.ZMsncFBH/kJsO', 'System Administrator', NULL, 1, 1, 1);

-- -----------------------------------------------------------------------------------------------------
-- Sample packages and add-ons (Admin maintains these in Settings)
-- -----------------------------------------------------------------------------------------------------
INSERT INTO packages (id, name, inclusions, price) VALUES
    (1, 'Basic Portrait',     '30-minute studio shoot, 1 backdrop, 5 edited photos, 2 pcs 5R prints',                      800.00),
    (2, 'Family Portrait',    '1-hour studio shoot, up to 8 persons, 2 backdrops, 15 edited photos, 1 pc 8R print',      2500.00),
    (3, 'Graduation Toga',    'Toga and creative shots, 10 edited photos, 2 pcs 5R prints, 4 wallet-size prints',        1200.00),
    (4, 'Birthday Coverage',  '4-hour event coverage, 1 photographer, all edited photos on USB, 20 pcs 4R prints',       6500.00),
    (5, 'Prenup Outdoor',     'Half-day outdoor shoot in Aparri, 2 outfit changes, 40 edited photos, 1 pc 12R print',  12000.00),
    (6, 'Wedding Essentials', 'Ceremony and reception coverage, 2 photographers, online gallery, 20-page photo book',  35000.00);

INSERT INTO addons (id, name, price) VALUES
    (1, 'Extra 5R print',               40.00),
    (2, 'Extra 8R print',              120.00),
    (3, 'Additional edited photo',      80.00),
    (4, '8x10 frame',                  350.00),
    (5, 'Rush editing (24 hours)',     500.00),
    (6, 'USB flash drive (32 GB)',     350.00),
    (7, 'Extra hour of coverage',      900.00),
    (8, 'Additional backdrop',         150.00);

-- -----------------------------------------------------------------------------------------------------
-- Sample bookings (dates relative to now so the demo always has upcoming and unpaid jobs)
-- -----------------------------------------------------------------------------------------------------
SET @y := YEAR(NOW());

INSERT INTO bookings (booking_no, customer_name, contact_no, customer_email, package_id, schedule_at,
                      downpayment, downpayment_method, downpayment_ref, status, notes, created_by) VALUES
    (CONCAT('BK-', @y, '-0001'), 'Maria Santos',      '09171234567', 'maria.santos@example.com', 2,
        DATE_FORMAT(DATE_SUB(NOW(), INTERVAL 2 DAY), '%Y-%m-%d 09:00:00'),  500.00, 'Cash',  NULL,            'Unpaid',    'Family of 6, white backdrop', 1),
    (CONCAT('BK-', @y, '-0002'), 'Juan Dela Cruz',    '09281234567', NULL,                        3,
        DATE_FORMAT(DATE_SUB(NOW(), INTERVAL 1 DAY), '%Y-%m-%d 13:30:00'),  300.00, 'GCash', '1029384756123', 'Unpaid',    'CSU Aparri graduate', 1),
    (CONCAT('BK-', @y, '-0003'), 'Angela Reyes',      '09391234567', 'angela.reyes@example.com', 1,
        DATE_FORMAT(NOW(), '%Y-%m-%d 10:00:00'),                               0.00, NULL,    NULL,            'Unpaid',    NULL, 1),
    (CONCAT('BK-', @y, '-0004'), 'Ramon Bautista',    '09451234567', NULL,                        4,
        DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 3 DAY), '%Y-%m-%d 15:00:00'), 2000.00, 'Maya',  'MY8765432109',  'Pending',   'Kids party, 7th birthday', 1),
    (CONCAT('BK-', @y, '-0005'), 'Liza and Mark Cruz','09561234567', 'lizamark@example.com',      5,
        DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 10 DAY), '%Y-%m-%d 06:00:00'), 5000.00, 'GCash', '5647382910456', 'Pending',  'Sunrise shoot at the Aparri beach', 1),
    (CONCAT('BK-', @y, '-0006'), 'Carlo Mendoza',     '09671234567', NULL,                        3,
        DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 1 DAY), '%Y-%m-%d 11:00:00'),    0.00, NULL,    NULL,            'Pending',   NULL, 1),
    (CONCAT('BK-', @y, '-0007'), 'Grace Villanueva',  '09781234567', 'grace.v@example.com',       6,
        DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 30 DAY), '%Y-%m-%d 08:00:00'), 10000.00, 'Cash', NULL,            'Pending',   'Church: St. Peter and Paul Parish', 1),
    (CONCAT('BK-', @y, '-0008'), 'Paolo Aquino',      '09891234567', NULL,                        1,
        DATE_FORMAT(DATE_SUB(NOW(), INTERVAL 5 DAY), '%Y-%m-%d 14:00:00'),    0.00, NULL,    NULL,            'Cancelled', 'Customer rescheduled to next month', 1);

-- Continue numbering after the sample bookings.
INSERT INTO doc_sequences (doc_type, seq_year, last_no) VALUES ('BK', @y, 8);

-- -----------------------------------------------------------------------------------------------------
-- Studio settings (edit in Settings > Business info). report_secret is generated by the app on first run.
-- -----------------------------------------------------------------------------------------------------
INSERT INTO settings (setting_key, setting_value) VALUES
    ('business_name',   'LENSYNC Photo Studio'),
    ('address',         'Centro, Aparri, Cagayan'),
    ('contact',         '0917 000 0000'),
    ('logo_path',       ''),
    ('signature_path',  ''),
    ('smtp_host',       'smtp.gmail.com'),
    ('smtp_port',       '587'),
    ('smtp_enable_ssl', 'true'),
    ('smtp_user',       ''),
    ('smtp_password',   ''),
    ('smtp_from_name',  'LENSYNC'),
    ('report_secret',   '');

INSERT INTO audit_logs (user_id, action, entity, details) VALUES
    (1, 'SEED', 'system', 'Database initialised with seed.sql');

COMMIT;
