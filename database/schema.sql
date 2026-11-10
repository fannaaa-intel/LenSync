-- =====================================================================================================
-- LENSYNC - Billing and Payment System for Photography Studios in Aparri
-- Full schema. IDEMPOTENT: drops and recreates every table (all data is lost). Run seed.sql afterwards.
--   mysql -u root --default-character-set=utf8mb4 -e "source database/schema.sql"
-- Conventions: lowercase_snake_case, INT AUTO_INCREMENT ids, InnoDB, utf8mb4_unicode_ci,
--              money DECIMAL(12,2), soft delete via is_active, created_at/updated_at on every table.
-- =====================================================================================================

CREATE DATABASE IF NOT EXISTS lensync_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE lensync_db;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS audit_logs;
DROP TABLE IF EXISTS report_logs;
DROP TABLE IF EXISTS transaction_items;
DROP TABLE IF EXISTS transactions;
DROP TABLE IF EXISTS bookings;
DROP TABLE IF EXISTS addons;
DROP TABLE IF EXISTS packages;
DROP TABLE IF EXISTS password_resets;
DROP TABLE IF EXISTS users;
DROP TABLE IF EXISTS role_permissions;
DROP TABLE IF EXISTS permissions;
DROP TABLE IF EXISTS roles;
DROP TABLE IF EXISTS settings;
DROP TABLE IF EXISTS doc_sequences;
SET FOREIGN_KEY_CHECKS = 1;

-- -----------------------------------------------------------------------------------------------------
-- Access control
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE roles (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    name         VARCHAR(50)  NOT NULL,
    description  VARCHAR(255) NULL,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_roles_name (name)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE permissions (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    perm_key     VARCHAR(40)  NOT NULL,              -- DASHBOARD, ADD_SERVICE, REPORTS, SETTINGS, VOID_SALE
    label        VARCHAR(100) NOT NULL,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_permissions_key (perm_key)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE role_permissions (
    role_id        INT      NOT NULL,
    permission_id  INT      NOT NULL,
    created_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (role_id, permission_id),
    CONSTRAINT fk_rp_role       FOREIGN KEY (role_id)       REFERENCES roles (id)       ON DELETE CASCADE,
    CONSTRAINT fk_rp_permission FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE users (
    id                    INT AUTO_INCREMENT PRIMARY KEY,
    username              VARCHAR(50)  NOT NULL,     -- case-insensitive unique (collation)
    password_hash         VARCHAR(100) NOT NULL,     -- BCrypt only, never plain text
    full_name             VARCHAR(120) NOT NULL,
    email                 VARCHAR(150) NULL,         -- registered email for Forgot Password
    role_id               INT          NOT NULL,
    is_active             TINYINT(1)   NOT NULL DEFAULT 1,
    must_change_password  TINYINT(1)   NOT NULL DEFAULT 0,
    failed_attempts       INT          NOT NULL DEFAULT 0,
    locked_until          DATETIME     NULL,         -- 5 failed attempts -> locked for 5 minutes
    last_login            DATETIME     NULL,
    created_at            DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at            DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_users_username (username),
    KEY ix_users_role (role_id),
    CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles (id),
    CONSTRAINT ck_users_failed CHECK (failed_attempts >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE password_resets (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    user_id     INT          NOT NULL,
    code_hash   VARCHAR(100) NOT NULL,               -- hashed 6-digit code
    expires_at  DATETIME     NOT NULL,               -- created_at + 10 minutes
    attempts    INT          NOT NULL DEFAULT 0,     -- max 5
    used_at     DATETIME     NULL,                   -- single use
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_pr_user (user_id, created_at),
    CONSTRAINT fk_pr_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Pricing
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE packages (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    name        VARCHAR(100)  NOT NULL,
    inclusions  TEXT          NULL,
    price       DECIMAL(12,2) NOT NULL,
    is_active   TINYINT(1)    NOT NULL DEFAULT 1,
    created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    KEY ix_packages_active (is_active, name),
    CONSTRAINT ck_packages_price CHECK (price >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE addons (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    name        VARCHAR(100)  NOT NULL,
    price       DECIMAL(12,2) NOT NULL,
    is_active   TINYINT(1)    NOT NULL DEFAULT 1,
    created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    KEY ix_addons_active (is_active, name),
    CONSTRAINT ck_addons_price CHECK (price >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Bookings (kept generic so an external booking system could write here later)
-- Status flow: Pending -> Unpaid (shoot done, awaiting billing) -> Paid / Cancelled
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE bookings (
    id                  INT AUTO_INCREMENT PRIMARY KEY,
    booking_no          VARCHAR(20)   NOT NULL,      -- BK-2026-0001
    customer_name       VARCHAR(120)  NOT NULL,
    contact_no          VARCHAR(20)   NULL,
    customer_email      VARCHAR(150)  NULL,
    package_id          INT           NULL,
    schedule_at         DATETIME      NOT NULL,
    downpayment         DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    downpayment_method  VARCHAR(10)   NULL,          -- Cash / GCash / Maya
    downpayment_ref     VARCHAR(50)   NULL,          -- required for e-wallets
    status              VARCHAR(10)   NOT NULL DEFAULT 'Pending',
    notes               TEXT          NULL,
    created_by          INT           NULL,
    is_active           TINYINT(1)    NOT NULL DEFAULT 1,
    created_at          DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_bookings_no (booking_no),
    KEY ix_bookings_status (status, schedule_at),
    KEY ix_bookings_schedule (schedule_at),
    KEY ix_bookings_customer (customer_name),
    CONSTRAINT fk_bookings_package FOREIGN KEY (package_id) REFERENCES packages (id),
    CONSTRAINT fk_bookings_user    FOREIGN KEY (created_by) REFERENCES users (id),
    CONSTRAINT ck_bookings_status  CHECK (status IN ('Pending', 'Unpaid', 'Paid', 'Cancelled')),
    CONSTRAINT ck_bookings_dp      CHECK (downpayment >= 0),
    CONSTRAINT ck_bookings_dp_m    CHECK (downpayment_method IS NULL OR downpayment_method IN ('Cash', 'GCash', 'Maya'))
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Sales
-- Total = sum(items) - discount; Balance due = Total - downpayment; Change = Tendered - Balance due
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE transactions (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    receipt_no       VARCHAR(20)   NOT NULL,         -- AR-2026-000001
    booking_id       INT           NOT NULL,
    subtotal         DECIMAL(12,2) NOT NULL,
    discount_type    VARCHAR(10)   NULL,             -- Fixed / Percent
    discount_value   DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    discount_amount  DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    discount_reason  VARCHAR(255)  NULL,
    total            DECIMAL(12,2) NOT NULL,
    downpayment      DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    balance_due      DECIMAL(12,2) NOT NULL,
    payment_method   VARCHAR(10)   NOT NULL,         -- Cash / GCash / Maya
    reference_no     VARCHAR(50)   NULL,             -- required for e-wallets
    amount_tendered  DECIMAL(12,2) NOT NULL,
    change_amount    DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    status           VARCHAR(10)   NOT NULL DEFAULT 'Completed',
    void_reason      VARCHAR(255)  NULL,
    voided_by        INT           NULL,
    voided_at        DATETIME      NULL,
    cashier_id       INT           NOT NULL,
    printed_count    INT           NOT NULL DEFAULT 0,
    created_at       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_transactions_receipt (receipt_no),
    KEY ix_transactions_created (created_at, status),
    KEY ix_transactions_booking (booking_id),
    KEY ix_transactions_cashier (cashier_id, created_at),
    KEY ix_transactions_method (payment_method, created_at),
    CONSTRAINT fk_tx_booking  FOREIGN KEY (booking_id) REFERENCES bookings (id),
    CONSTRAINT fk_tx_cashier  FOREIGN KEY (cashier_id) REFERENCES users (id),
    CONSTRAINT fk_tx_voidedby FOREIGN KEY (voided_by)  REFERENCES users (id),
    CONSTRAINT ck_tx_status   CHECK (status IN ('Completed', 'Voided')),
    CONSTRAINT ck_tx_method   CHECK (payment_method IN ('Cash', 'GCash', 'Maya')),
    CONSTRAINT ck_tx_disc     CHECK (discount_type IS NULL OR discount_type IN ('Fixed', 'Percent')),
    CONSTRAINT ck_tx_amounts  CHECK (subtotal >= 0 AND discount_amount >= 0 AND total >= 0 AND downpayment >= 0
                                     AND amount_tendered >= 0 AND change_amount >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

CREATE TABLE transaction_items (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    transaction_id  INT           NOT NULL,
    item_type       VARCHAR(10)   NOT NULL,          -- Package / Addon / Other
    ref_id          INT           NULL,              -- packages.id or addons.id; NULL for Other
    description     VARCHAR(255)  NOT NULL,          -- snapshot of the name at time of sale
    qty             INT           NOT NULL DEFAULT 1,
    unit_price      DECIMAL(12,2) NOT NULL,          -- snapshot of the price at time of sale
    line_total      DECIMAL(12,2) NOT NULL,
    created_at      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_items_tx (transaction_id),
    CONSTRAINT fk_items_tx   FOREIGN KEY (transaction_id) REFERENCES transactions (id),
    CONSTRAINT ck_items_type CHECK (item_type IN ('Package', 'Addon', 'Other')),
    CONSTRAINT ck_items_qty  CHECK (qty > 0),
    CONSTRAINT ck_items_amt  CHECK (unit_price >= 0 AND line_total >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Reports (proof of authenticity: content_hash + HMAC verification_code)
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE report_logs (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    report_no          VARCHAR(20)   NOT NULL,       -- RPT-2026-0001
    report_type        VARCHAR(40)   NOT NULL,
    date_from          DATE          NOT NULL,
    date_to            DATE          NOT NULL,
    filters_json       TEXT          NULL,
    row_count          INT           NOT NULL DEFAULT 0,
    total_amount       DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    content_hash       CHAR(64)      NOT NULL,       -- SHA-256 hex of the report data
    verification_code  VARCHAR(16)   NOT NULL,       -- first 10 chars (Base32) of HMAC-SHA256
    generated_by       INT           NOT NULL,
    printer_name       VARCHAR(255)  NULL,
    generated_at       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at         DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_reports_no (report_no),
    KEY ix_reports_code (verification_code),
    KEY ix_reports_generated (generated_at),
    CONSTRAINT fk_reports_user FOREIGN KEY (generated_by) REFERENCES users (id),
    CONSTRAINT ck_reports_range CHECK (date_to >= date_from)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Audit trail (append-only)
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE audit_logs (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    user_id     INT          NULL,                   -- NULL for e.g. a failed login with an unknown username
    action      VARCHAR(50)  NOT NULL,               -- LOGIN, LOGOUT, LOGIN_FAILED, USER_CREATE, DISCOUNT, VOID, ...
    entity      VARCHAR(50)  NULL,
    entity_id   INT          NULL,
    details     TEXT         NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_audit_created (created_at),
    KEY ix_audit_user (user_id, created_at),
    KEY ix_audit_action (action, created_at),
    CONSTRAINT fk_audit_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Settings (one studio per installation)
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE settings (
    setting_key    VARCHAR(64) NOT NULL PRIMARY KEY,
    setting_value  TEXT        NULL,
    created_at     DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------------------------------
-- Document number counters (BK / AR / RPT per year). Incremented inside the same DB transaction as the
-- insert (Db.NextDocumentNo) so two cashiers can never get the same number.
-- -----------------------------------------------------------------------------------------------------
CREATE TABLE doc_sequences (
    doc_type    VARCHAR(10) NOT NULL,
    seq_year    SMALLINT    NOT NULL,
    last_no     INT         NOT NULL DEFAULT 0,
    created_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (doc_type, seq_year)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;
