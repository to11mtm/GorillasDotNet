const KEY = 'gorillas.playerId';
const NICK = 'gorillas.nickname';

export function playerId() {
    let id = localStorage.getItem(KEY);
    if (!id) {
        id = (crypto.randomUUID && crypto.randomUUID()) || `p-${Date.now()}-${Math.random().toString(16).slice(2)}`;
        localStorage.setItem(KEY, id);
    }
    return id;
}

export function nickname() {
    return localStorage.getItem(NICK) ?? '';
}

export function rememberNickname(value) {
    localStorage.setItem(NICK, value ?? '');
}
