// Theme toggle: manages dark/light mode via 'dark' class on <html>
window.themeInterop = {
    init: function () {
        const saved = localStorage.getItem('theme');
        if (saved === 'dark' || (!saved && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
            document.documentElement.classList.add('dark');
            return true; // isDark
        }
        document.documentElement.classList.remove('dark');
        return false;
    },
    toggle: function () {
        const isDark = document.documentElement.classList.toggle('dark');
        localStorage.setItem('theme', isDark ? 'dark' : 'light');
        return isDark;
    },
    isDark: function () {
        return document.documentElement.classList.contains('dark');
    }
};
