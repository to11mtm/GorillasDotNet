export async function copy(text) {
    if (!text) {
        return false;
    }

    // The async clipboard API needs a secure context, which rules out plain-HTTP LAN play —
    // exactly the case where someone is most likely to be sharing a link.
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Permission denied or no user gesture; fall through to the legacy path.
        }
    }

    try {
        const scratch = document.createElement('textarea');
        scratch.value = text;
        scratch.setAttribute('readonly', '');
        scratch.style.position = 'fixed';
        scratch.style.opacity = '0';
        document.body.appendChild(scratch);
        scratch.select();

        const copied = document.execCommand('copy');
        document.body.removeChild(scratch);
        return copied;
    } catch {
        return false;
    }
}
