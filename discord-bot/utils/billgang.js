const axios = require('axios');

const BASE_URL = 'https://pg-api.billgang.com/v1';

/**
 * Billgang API client
 * Docs: https://developers.billgang.com
 */
class BillgangClient {
  constructor(apiKey, shopSlug) {
    this.apiKey = apiKey;
    this.shopSlug = shopSlug;
    this.http = axios.create({
      baseURL: BASE_URL,
      headers: {
        Authorization: `Bearer ${apiKey}`,
        'Content-Type': 'application/json',
      },
      timeout: 10_000,
    });
  }

  /**
   * Fetch an order by its ID from the Billgang API.
   * Returns the order object if found, null if not found (404),
   * or throws on other errors.
   *
   * @param {string} orderId
   * @returns {Promise<object|null>}
   */
  async getOrder(orderId) {
    try {
      const response = await this.http.get(`/shops/${this.shopSlug}/orders/${orderId}`);
      return response.data;
    } catch (err) {
      if (err.response?.status === 404) return null;
      // Re-throw network/auth errors so callers can handle them
      throw err;
    }
  }

  /**
   * List recent orders (useful for admin verification or bulk import).
   * @param {number} page
   * @returns {Promise<object>}
   */
  async listOrders(page = 1) {
    const response = await this.http.get(`/shops/${this.shopSlug}/orders`, {
      params: { page, limit: 50 },
    });
    return response.data;
  }
}

module.exports = BillgangClient;
