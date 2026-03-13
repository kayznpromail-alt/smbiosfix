const { SlashCommandBuilder, EmbedBuilder, Colors } = require('discord.js');
const {
  getVerifiedOrder,
  saveVerifiedOrder,
  logAttempt,
  countRecentAttempts,
} = require('../utils/database');

const RATE_LIMIT_ATTEMPTS = 5;   // max attempts
const RATE_LIMIT_WINDOW_MS = 60_000; // per 60s

module.exports = {
  data: new SlashCommandBuilder()
    .setName('verify')
    .setDescription('Vérifie ton order ID Billgang pour prouver ton achat')
    .addStringOption((opt) =>
      opt
        .setName('order_id')
        .setDescription('Ton Order ID Billgang (ex: ORD-XXXXXXXX)')
        .setRequired(true)
    ),

  async execute(interaction, billgang) {
    await interaction.deferReply({ ephemeral: true });

    const orderId = interaction.options.getString('order_id').trim();
    const userId = interaction.user.id;
    const guildId = interaction.guildId;

    // ─── Rate limiting ────────────────────────────────────────────────────────
    const attempts = countRecentAttempts(userId, RATE_LIMIT_WINDOW_MS);
    if (attempts >= RATE_LIMIT_ATTEMPTS) {
      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Red)
            .setTitle('⏳ Trop de tentatives')
            .setDescription(
              `Tu as déjà essayé ${RATE_LIMIT_ATTEMPTS} fois en moins d'une minute.\nReessaie dans quelques secondes.`
            ),
        ],
      });
    }

    // ─── Already verified by someone? ────────────────────────────────────────
    const existing = getVerifiedOrder(orderId);
    if (existing) {
      logAttempt(orderId, userId, guildId, false);

      const alreadyYou = existing.user_id === userId;
      const verifiedAt = new Date(existing.verified_at).toLocaleString('fr-FR');

      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Orange)
            .setTitle('⚠️ Order déjà vérifié')
            .setDescription(
              alreadyYou
                ? `Tu as déjà vérifié cet order le **${verifiedAt}**.`
                : `Cet order a déjà été claim par quelqu'un d'autre le **${verifiedAt}**.\nSi tu penses que c'est une erreur, contacte un admin.`
            )
            .addFields({ name: 'Order ID', value: `\`${orderId}\`` }),
        ],
      });
    }

    // ─── Call Billgang API ────────────────────────────────────────────────────
    let order;
    try {
      order = await billgang.getOrder(orderId);
    } catch (err) {
      logAttempt(orderId, userId, guildId, false);
      console.error('[verify] Billgang API error:', err.message);

      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Red)
            .setTitle('❌ Erreur API')
            .setDescription(
              "Impossible de joindre l'API Billgang pour l'instant. Réessaie dans quelques instants."
            ),
        ],
      });
    }

    // Order doesn't exist on Billgang
    if (!order) {
      logAttempt(orderId, userId, guildId, false);

      return interaction.editReply({
        embeds: [
          new EmbedBuilder()
            .setColor(Colors.Red)
            .setTitle('❌ Order introuvable')
            .setDescription(
              `Aucun order avec l'ID \`${orderId}\` n'a été trouvé sur Billgang.\nVérifie que tu as bien copié ton order ID depuis ta confirmation d'achat.`
            ),
        ],
      });
    }

    // ─── Save & give role ────────────────────────────────────────────────────
    saveVerifiedOrder(
      orderId,
      userId,
      interaction.user.tag,
      guildId,
      order
    );
    logAttempt(orderId, userId, guildId, true);

    // Optionally assign a buyer role
    const buyerRoleId = process.env.BUYER_ROLE_ID;
    let roleMsg = '';
    if (buyerRoleId) {
      try {
        const member = await interaction.guild.members.fetch(userId);
        await member.roles.add(buyerRoleId);
        roleMsg = `\nLe rôle <@&${buyerRoleId}> t'a été attribué automatiquement.`;
      } catch {
        roleMsg = '\n*(Le rôle acheteur n\'a pas pu être assigné — vérifie les permissions du bot.)*';
      }
    }

    // Build a nice success embed with order details
    const embed = new EmbedBuilder()
      .setColor(Colors.Green)
      .setTitle('✅ Order vérifié avec succès !')
      .setDescription(`Ton achat a bien été confirmé.${roleMsg}`)
      .addFields(
        { name: 'Order ID', value: `\`${orderId}\``, inline: true },
        {
          name: 'Produit',
          value: order.product?.name ?? order.items?.[0]?.name ?? 'N/A',
          inline: true,
        },
        {
          name: 'Montant',
          value: order.total != null
            ? `${order.total} ${order.currency ?? ''}`.trim()
            : 'N/A',
          inline: true,
        },
        {
          name: 'Date d\'achat',
          value: order.created_at
            ? new Date(order.created_at).toLocaleString('fr-FR')
            : 'N/A',
          inline: true,
        }
      )
      .setFooter({ text: 'Billgang Order Verifier' })
      .setTimestamp();

    return interaction.editReply({ embeds: [embed] });
  },
};
