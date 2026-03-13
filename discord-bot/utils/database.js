const Database = require('better-sqlite3');
const path = require('path');

const DB_PATH = path.join(__dirname, '..', 'data', 'orders.db');

// Ensure the data directory exists
const fs = require('fs');
fs.mkdirSync(path.dirname(DB_PATH), { recursive: true });

const db = new Database(DB_PATH);

// Run migrations
db.exec(`
  CREATE TABLE IF NOT EXISTS verified_orders (
    order_id     TEXT PRIMARY KEY,
    user_id      TEXT NOT NULL,
    username     TEXT NOT NULL,
    guild_id     TEXT NOT NULL,
    verified_at  INTEGER NOT NULL,
    order_data   TEXT
  );

  CREATE TABLE IF NOT EXISTS verification_attempts (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id     TEXT NOT NULL,
    user_id      TEXT NOT NULL,
    guild_id     TEXT NOT NULL,
    attempted_at INTEGER NOT NULL,
    success      INTEGER NOT NULL
  );
`);

/**
 * Returns the verified order record if this order has already been claimed,
 * otherwise returns undefined.
 * @param {string} orderId
 */
function getVerifiedOrder(orderId) {
  return db.prepare('SELECT * FROM verified_orders WHERE order_id = ?').get(orderId);
}

/**
 * Saves a verified order to the database.
 * @param {string} orderId
 * @param {string} userId     - Discord user ID who claimed it
 * @param {string} username   - Discord username
 * @param {string} guildId
 * @param {object} orderData  - Raw order object from Billgang API
 */
function saveVerifiedOrder(orderId, userId, username, guildId, orderData) {
  db.prepare(`
    INSERT INTO verified_orders (order_id, user_id, username, guild_id, verified_at, order_data)
    VALUES (?, ?, ?, ?, ?, ?)
  `).run(orderId, userId, username, guildId, Date.now(), JSON.stringify(orderData));
}

/**
 * Records a verification attempt (successful or not).
 */
function logAttempt(orderId, userId, guildId, success) {
  db.prepare(`
    INSERT INTO verification_attempts (order_id, user_id, guild_id, attempted_at, success)
    VALUES (?, ?, ?, ?, ?)
  `).run(orderId, userId, guildId, Date.now(), success ? 1 : 0);
}

/**
 * Count how many times a user has attempted verification in the last `windowMs` ms.
 * Used for rate-limiting.
 */
function countRecentAttempts(userId, windowMs = 60_000) {
  const since = Date.now() - windowMs;
  const row = db
    .prepare('SELECT COUNT(*) as cnt FROM verification_attempts WHERE user_id = ? AND attempted_at > ?')
    .get(userId, since);
  return row.cnt;
}

/**
 * Global stats for the /stats command.
 */
function getStats(guildId) {
  const total = db.prepare('SELECT COUNT(*) as cnt FROM verified_orders WHERE guild_id = ?').get(guildId).cnt;
  const today = db
    .prepare(
      'SELECT COUNT(*) as cnt FROM verified_orders WHERE guild_id = ? AND verified_at > ?'
    )
    .get(guildId, Date.now() - 86_400_000).cnt;
  return { total, today };
}

/**
 * Returns all verified orders for a specific Discord user in this guild.
 */
function getOrdersByUser(userId, guildId) {
  return db
    .prepare('SELECT * FROM verified_orders WHERE user_id = ? AND guild_id = ?')
    .all(userId, guildId);
}

/**
 * Admin: delete a verified order (to allow re-verification).
 */
function deleteVerifiedOrder(orderId) {
  return db.prepare('DELETE FROM verified_orders WHERE order_id = ?').run(orderId);
}

module.exports = {
  getVerifiedOrder,
  saveVerifiedOrder,
  logAttempt,
  countRecentAttempts,
  getStats,
  getOrdersByUser,
  deleteVerifiedOrder,
};
