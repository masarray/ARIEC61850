const navToggle = document.querySelector(".nav-toggle");
const navLinks = document.querySelector(".nav-links");

if (navToggle && navLinks) {
  navToggle.addEventListener("click", () => {
    const isOpen = navLinks.classList.toggle("is-open");
    navToggle.setAttribute("aria-expanded", String(isOpen));
  });

  navLinks.addEventListener("click", (event) => {
    if (event.target instanceof HTMLAnchorElement) {
      navLinks.classList.remove("is-open");
      navToggle.setAttribute("aria-expanded", "false");
    }
  });
}

const tabs = document.querySelectorAll("[data-command]");
const panels = document.querySelectorAll("[data-command-panel]");

tabs.forEach((tab) => {
  tab.addEventListener("click", () => {
    const command = tab.getAttribute("data-command");

    tabs.forEach((item) => item.classList.toggle("is-active", item === tab));
    panels.forEach((panel) => {
      panel.classList.toggle("is-hidden", panel.getAttribute("data-command-panel") !== command);
    });
  });
});
