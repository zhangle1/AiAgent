import kunBuddyLogo from "../assets/brand/kunbuddy.png";

export function HomeMascotLogo() {
  return (
    <span
      className="home-mascot-logo"
      data-testid="home-mascot-logo"
      aria-hidden="true"
    >
      <img
        className="home-mascot-mark"
        src={kunBuddyLogo}
        alt=""
        width={100}
        height={100}
        draggable={false}
      />
    </span>
  );
}
