/// <reference path="jquery-3.4.1.min.js" />
/// <reference path="jquery.validate.min.js" />
/// <reference path="jquery.validate.unobtrusive.min.js" />

(function () {
    'use strict';

    // ===== GO TO TOP BUTTON FUNCTIONALITY =====
    const goTopBtn = document.getElementById('goTopBtn');

    // Show/hide button based on scroll position
    window.addEventListener('scroll', function () {
        if (!goTopBtn) return;
        if (window.pageYOffset > 300) {
            goTopBtn.classList.add('show');
        } else {
            goTopBtn.classList.remove('show');
        }
    }, { passive: true });

    // Smooth scroll to top function
    window.scrollToTop = function () {
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    };

    // ===== IMPROVED CHAT FUNCTIONALITY =====
    const autoResponses = {
        'cena': '💰 <strong>Cenové informácie:</strong><br>Naše produkty majú bezkonkurenčné ceny. Všetky ceny nájdete na stránke Produkty. Použite na stránke Stripe kód LETOJETU26 pre 10% zľavu!',
        'doprava': '🚚 <strong>Doprava:</strong><br>• <strong>Kurier na adresu</strong><br>• <strong>GLS kurier</strong>, <strong>GLS ParcelShop</strong> a <strong>GLS Box</strong><br>• <strong>AlzaBox</strong><br>• <strong>DPD kurier</strong> a <strong>DPD odberne miesto</strong> na Slovensku<br>• <strong>Packeta Z-BOX</strong> a Packeta odberne miesta na Slovensku<br>• Cena dopravy sa vypocita automaticky podla zvolenej sluzby a hodnoty kosika<br>• Po odoslani objednavky vam pride e-mail so suhrnom a dalsimi krokmi',
        'platba': '💳 <strong>Bezpečná Platba:</strong><br>Používame platby cez Stripe alebo odoslať objednávku cez bankový prevod na stránke košíka.',
        'vratenie': '↩️ <strong>Vrátenie Tovarov:</strong><br>• ✓ 120 dní na vrátenie<br>• ✓ Bez otázok<br>• ✓ Bezplatne',
        'produkty': '🛍️ <strong>Naša Ponuka:</strong><br>BalanceOil | Zinobiotic | CollagenBoozt | Vitamin D Test',
        'balanceoil': '⭐ <strong>BalanceOil:</strong><br>Prírodný Omega-3 olej. Podporuje srdce, mozog a zrak. Dostupný: 300ml.',
        'it servis': '🖥️ <strong>IT servis:</strong><br>• Diagnostika a opravy PC / notebookov<br>• Inštalácia a nastavenie Windows + programov<br>• Odvirovanie, zrýchlenie PC, zálohy<br>• Upgrade komponentov (SSD/RAM) a poradenstvo<br><br>Napíšte stručne problém (napr. „PC sa nezapne“, „je pomalý“, „vírus“) a ozveme sa vám.',
        'web': '🌐 <strong>Webové služby:</strong><br>• Návrh a tvorba webstránok<br>• Úpravy existujúcich webov, SEO základ<br>• Hosting a technická správa<br><br>Pošlite odkaz na web alebo predstavu a pripravíme návrh riešenia.',
        'zlava': '🎁 <strong>Zľava LETOJETU26:</strong><br>10% zľava na vybrané produkty nad 50€. Použite pri checkout!',
        'kontakt': '📞 <strong>Kontaktujte Nás:</strong><br>☎️ +421917952432<br>📧 info@ladowebservis.sk<br>⏰ Po-Pia: 9:00-19:00',
        'pomoc': '❓ <strong>Ako ti pomôžem?</strong><br>Napíš: cena, doprava, platba, produkty, zľava, vratenie alebo kontakt',
        'bonusové body': '⭐ <strong>Bonusové Body:</strong><br>Za každých €10 nákupu dostaneš 1 bod. 100 bodov = €10 zľava!',
        'program': '🎯 <strong>Vernostný Program:</strong><br>Zbieraj body, získavaj zľavy a exkluzívne ponuky!'

    };

    // Generate products grid for side cart - REMOVED
    function renderSideCartProducts() {
        // Products section has been removed from side cart
        return;
    }

    // Add product to cart - REMOVED
    window.addProductToCart = function(productId) {
        // Products section has been removed from side cart
        return;
    };

    // Send chat message
    window.sendChatMessage = function() {
        const chatInput = document.getElementById('chat-input');
        const chatMessages = document.getElementById('chat-messages');

        if (!chatInput || !chatMessages) return;

        const message = chatInput.value.trim();
        if (!message) return;

        // Add user message
        const userDiv = document.createElement('div');
        userDiv.className = 'chat-message user';
        userDiv.innerHTML = '<strong>👤 Vy:</strong> ' + escapeHtml(message);
        chatMessages.appendChild(userDiv);

        chatInput.value = '';
        chatInput.focus();
        chatMessages.scrollTop = chatMessages.scrollHeight;

        // Simulate bot typing
        setTimeout(function() {
            const response = getAutoResponse(message);
            const botDiv = document.createElement('div');
            botDiv.className = 'chat-message bot';
            botDiv.innerHTML = '<strong>🤖 Support:</strong> ' + response;
            chatMessages.appendChild(botDiv);
            chatMessages.scrollTop = chatMessages.scrollHeight;
        }, 300);
    };

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function getAutoResponse(input) {
        const query = input.toLowerCase().trim();

        // Direct match
        if (autoResponses[query]) {
            return autoResponses[query];
        }

        // Partial match - check each keyword
        for (const key in autoResponses) {
            if (query.includes(key)) {
                return autoResponses[key];
            }
        }

        // Czech/Slovak aliases
        if (query.includes('peniaze') || query.includes('penize')) return autoResponses['cena'];
        if (query.includes('vzorka') || query.includes('vzorky')) return autoResponses['produkty'];
        if (query.includes('porada')) return autoResponses['pomoc'];
        if (query.includes('zásielkovna') || query.includes('zasielkovna') || query.includes('packeta')) return autoResponses['doprava'];
        if (query.includes('kuriér') || query.includes('kurier') || query.includes('adresu') || query.includes('adresa')) return autoResponses['doprava'];
        if (query.includes('it') || query.includes('servis pc') || query.includes('pc servis') || query.includes('notebook') || query.includes('windows') || query.includes('oprava')) return autoResponses['it servis'];
        if (query.includes('web') || query.includes('stránka') || query.includes('stranky') || query.includes('webstránka') || query.includes('webstranka') || query.includes('hosting') || query.includes('seo')) return autoResponses['web'];

        // Default response
        return '😊 Pomôžem ti rád! Napíš niečo z: <strong>cena, doprava, platba, produkty, zľava, vratenie, kontakt</strong>';
    }

    // Initialize chat when DOM is ready
    document.addEventListener('DOMContentLoaded', function() {
        // Products section has been removed - no need to render

        // Add enter key support to chat input
        const chatInput = document.getElementById('chat-input');
        if (chatInput) {
            chatInput.addEventListener('keypress', function(e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendChatMessage();
                }
            });
        }

        // Display welcome message
        const chatMessages = document.getElementById('chat-messages');
        if (chatMessages && chatMessages.children.length === 0) {
            const welcomeDiv = document.createElement('div');
            welcomeDiv.className = 'chat-message bot';
            welcomeDiv.innerHTML = '<strong>🤖 Support:</strong> Ahoj! 👋 Ako ti môžem pomôcť? Napíš <strong>pomoc</strong> pre ponuku otázok.';
            chatMessages.appendChild(welcomeDiv);
        }
    });

})();
